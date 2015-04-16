using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Collections;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace SntpDemo
{
	public class Sntpv4Client
	{
		public int TimeoutMilliseconds
		{
			get
			{
				lock (timeoutMillisecondsLocker)
				{
					return timeoutMilliseconds;
				}
			}
			set
			{
				lock (timeoutMillisecondsLocker)
				{
					timeoutMilliseconds = value;
				}
			}
		}
		public int RetriesFromFailure
		{
			get
			{
				lock (retriesFromFailureLocker)
				{
					return retriesFromFailure;
				}
			}
			set
			{
				lock (retriesFromFailureLocker)
				{
					retriesFromFailure = value;
				}
			}
		}
		public int MillisecondsBetweenRetries
		{
			get
			{
				lock (millisecondsBetweenRetriesLocker)
				{
					return millisecondsBetweenRetries;
				}
			}
			set
			{
				lock (millisecondsBetweenRetriesLocker)
				{
					millisecondsBetweenRetries = value;
				}
			}
		}
		public Boolean UpdateSystemClockWhenSynced
		{
			get
			{
				lock (updateSystemClockWhenSyncedLocker)
				{
					return updateSystemClockWhenSynced;
				}
			}
			set
			{
				lock (updateSystemClockWhenSyncedLocker)
				{
					updateSystemClockWhenSynced = value;
				}
			}
		}


		public Boolean SyncRunning//true if sync is in progress 
		{ //locking not needed because the variable is volatile and is externally only read
			get
			{
				return syncRunning;
			}
		}
		public Boolean SyncOK //true if the last sync with server was OK (false otherwise or if no sync occurred at all)
		{ //locking not needed because the variable is volatile and is externally only read
			get
			{
				return syncOK;
			}
		}
		public Boolean ClockUpdatedOK  //true if the system clock update was OK, turns false with a new sync attempt
		{//locking not needed because the variable is volatile and is externally only read
			get
			{
				return clockUpdatedOK;
			}
		}
		public NtpPacket NtpPacketFromSync
		{
			get
			{
				return ntpPacketFromSync.DeepClone();
			}
		}

		private String ntpServer;
		private int ntpPort = 123;
		private NtpPacket ntpPacketFromSync;
		private Sntpv4ClientQuery sntpClientQuery; //note: do not lock against this as it may be replaced frequently
		private BooleanWrapper saveResult; //when a thread doing the timesync is abandoned, the referenced object’s value is set to false and then the reference is replaced with a new reference to a new BooleanWrapper
		private BooleanWrapper timeoutStopwatchKillSyncOnTimeout;

		//timeoutMillisecondsLocker always locks _write_ access to timeoutMilliseconds
		private readonly Object timeoutMillisecondsLocker = new Object();
		private volatile int timeoutMilliseconds;
		//retriesFromFailureLocker always locks _write_ access to retriesFromFailure
		private readonly Object retriesFromFailureLocker = new Object();
		private volatile int retriesFromFailure;
		//millisecondsBetweenRetriesLocker always locks _write_ access to millisecondsBetweenRetries
		private readonly Object millisecondsBetweenRetriesLocker = new Object();
		private volatile int millisecondsBetweenRetries;
		//updateSystemClockWhenSyncedLocker always locks _write_ access to updateSystemClockWhenSynced
		private readonly Object updateSystemClockWhenSyncedLocker = new Object();
		private volatile Boolean updateSystemClockWhenSynced;
		private volatile Boolean syncRunning; //true if sync is in progress
		private volatile Boolean syncOK; //true if the last sync with server was OK (false otherwise or if no sync occurred at all)
		private volatile Boolean clockUpdatedOK; //true if the system clock update was OK, turns false with a new sync attempt

		//All locks have an exact list of locked items specified so that is is easier to perform analysis of correctness.

		/*
         * syncLocker always locks the following variables (for specified actions):
         *  syncRunning (writing)
         *  saveResult (writing)
         *  syncOK (writing)
         *  clockUpdateOK (writing)
         *  sntpClientQuery (all interaction except sntpClientQuery.AskServer())
         *  timeoutStopwatchKillSyncOnTimeout (writing)
         *  ntpPacketFromSync (writing)
         *  numOfRetriesPerformed (all access)
         * 
         * Please note that saveResult and timeoutStopwatchKillSyncOnTimeout are references to objects that are
         * replaced with new instances for each sync(). That means there can be several abandoned sync()s and
         * timeoutStopwatch()es that can resolve what to do only serially, one-by-one. The use of per-class instance
         * locks for independent per-thread object locking can be a bottleneck in scenarios where high parallel
         * throughput is needed. Although it should be possible to create a correct version with proper fine-grained
         * locks, it would be unnecessarily complex, error-prone, hard to prove and unnecessary for this sntp sync
         * scenario. Moreover, the abandoned threads have only few instructions to execute before releasing
         * the lock (and dying) and thus the performance impact of a simple design is negligible in this case.
         */
		private readonly Object syncLocker = new Object();

		//startSyncAlreadyCalledLocker always locks all access to startSyncAlreadyCalled
		private readonly Object startSyncAlreadyCalledLocker = new Object();
		private volatile Boolean startSyncAlreadyCalled = false;

		//stopSyncAlreadyCalledLocker always locks all access to stopSyncAlreadyCalled       
		private readonly Object stopSyncAlreadyCalledLocker = new Object();
		private volatile Boolean stopSyncAlreadyCalled = false;

		private int numOfRetriesPerformed = 0;


		public Sntpv4Client(String NtpServer)
		{
			ntpServer = NtpServer;
			initializeProperties();
		}

		public Sntpv4Client(String NtpServer, int NtpPort)
		{
			ntpServer = NtpServer;
			ntpPort = NtpPort;
			initializeProperties();
		}

		private void initializeProperties()
		{
			lock (syncLocker) //for meticulousness 
			{
				syncRunning = false;
				syncOK = false;
				clockUpdatedOK = false;
				timeoutMilliseconds = Constants.SntpNetworkTimeoutMs;
				retriesFromFailure = 0;
				millisecondsBetweenRetries = Constants.SntpTimeBetweenRetriesMs;
				updateSystemClockWhenSynced = false;
			}
		}

		public void StartSyncAndBlockUntilCompleted()
		{
			StartSync(); //does nothing if already started
			//if StartSync() sets syncRunning to true, then it always happens before StartSync() returns

			Boolean live = true;

			while (live)
			{
				lock (syncLocker) //this is the lock that is locked by sync() and thus it waits until its completition 
				{
					live = syncRunning;
				}
				Thread.Sleep(50);
			}
		}

		public void StartSync()
		{
			if (!syncRunning)
			{
				//Short comment: This check is only a speed up and does not change the functionality in any way.

				//Long comment:
				//Quickly end execution if the sync is running; please note that although testing a boolean value is
				//quicker than waiting for a lock, there could be a race condition when several concurrently called StartSync()s enter
				//this section at the same time. For this tiny fraction of (possibly) concurrently started StartSync()s, the following
				//Boolean field and a lock ensure that only one StartSync() continues execution and the others do not event
				//ATTEMPT to obtain the syncLocker lock and exit immediately after detecting they were not the first, without queueing
				//and waiting for the syncLocker lock. The lack of queued requests for syncLocker lock makes StartSync() and
				//StopSync() behave correctly. The very first check in this method only speeds up some requests so that they do not have
				//to lock and unlock startSyncAlreadyCalledLocker.

				Boolean continueExecution = false;
				lock (startSyncAlreadyCalledLocker)
				{
					if (startSyncAlreadyCalled)
					{//someone else already called StartSync()... better exit quickly
						continueExecution = false;
					}
					else
					{
						//no one called StartSync() yet
						continueExecution = true;
						startSyncAlreadyCalled = true; //note that it was just called
					}
					//At this point we certainly know whether or not we are allowed to execute further. Releasing the
					//lock so that others can quickly discover whether they can run.
				}

				if (continueExecution)
				{
					lock (syncLocker)
						//By allowing to execute further only after obtaining a lock, it is ensured that the value of SyncRunning is finally tested
						//only once at a time, with the other threads running StopSync() waiting for the lock (should there be any).
					{

						if (!syncRunning) //continue only if the sync is not launched yet, volatile makes sure that the value is current and it is sufficient since there are no concurrent writes to the property
						{

							syncRunning = true; //updating this property to make all potential waiting StartSync()s to exit after obtaining the lock
							//updating this property only after obtaining a lock makes it impossible for two concurrently started StartSync()s to edit it
							syncOK = false; //invalidate previous results since we are just starting a new sync
							clockUpdatedOK = false; //invalidate previous results since we are just starting a new sync

							sntpClientQuery = new Sntpv4ClientQuery(ntpServer, ntpPort);
							sntpClientQuery.ReceiveTimeout = timeoutMilliseconds; //make the thread exit by itself by the time the controlling thread wants to kill it already

							//sync thread

							//Q: Why the hell is this method correct only with this use of local variables?
							//A: Because syncThread can finish before timeoutThread starts, deleting the global reference before it is even passed to the timeoutThread.
							BooleanWrapper timeoutKillLocal = new BooleanWrapper(true);
							timeoutStopwatchKillSyncOnTimeout = timeoutKillLocal;
							BooleanWrapper saveResultLocal = new BooleanWrapper(true);
							saveResult = saveResultLocal;
							Sntpv4ClientQuery localQuery = sntpClientQuery;


							//these writes will be observed correctly from this thread but may be observed incorrectly
							//from the thread that is about to be started
							Thread.MemoryBarrier(); //the memory barrier flushes all writes

							Thread syncThread = new Thread(
								delegate()
								{
								sync(saveResultLocal, timeoutKillLocal, localQuery); //pass the thread its correct BooleanWrapper reference*
							}
							);
							syncThread.IsBackground = true; //kill the thread when the app exits
							syncThread.Start();

							//timer thread
							Thread timeoutThread = new Thread(
								delegate()
								{
								timeoutStopwatch(timeoutKillLocal, timeoutMilliseconds); //pass the thread its correct BooleanWrapper reference*
							}
							);
							timeoutThread.IsBackground = true; //kill the thread when the app exits
							timeoutThread.Start();


							// *: There is no guarantee that the thread does not wait very long time for the start, although it would
							//be very unusual. Passing the BooleanWrapper reference makes sure each thread gets its own correct reference
							//(and not a reference intended for the thread launched in 5 minutes).
							//With that mechanism in place, it is possible to abandon a thread before it even started itself.
							//sync() does not need the timeoutStopwatchKillSyncOnTimeout reference passed because it accesses it
							//iff saveResult is true, in which case the thread is not abandoned and it is the only unabandoned sync()
							//thread for the class instance, making all class instance’s global variables relevant for sync() and timeoutStopwatch().
							//The same with sntpClientQuery and timeoutMilliseconds - ensuring the freshness of the values.

						}
					}
					lock (startSyncAlreadyCalledLocker)
					{
						startSyncAlreadyCalled = false;
						//since we are the one who was the first and we are just terminating, note that there is no one running any longer
					}
				}
			}
		}

		public void StopSync()
		{
			//(aborting the thread with Sntpv4ClientQuery.AskServer() is not an option because it can cause a lot of problems)

			if (syncRunning) //quickly filter out irrelevant calls
			{//read StartSync() which is written similarly to StopSync() for more comments

				Boolean continueExecution = false;
				lock (stopSyncAlreadyCalledLocker)
				{
					if (stopSyncAlreadyCalled)
					{
						continueExecution = false;
					}
					else
					{
						continueExecution = true;
						stopSyncAlreadyCalled = true;
					}
					//At this point we certainly know whether or not we are allowed to execute further. Releasing the
					//lock so that others can quickly discover whether they can run.
				}

				if (continueExecution)
				{
					lock (syncLocker)
					{

						//the lock ensures that 1) there is no StartSync() attempt and 2) the about-to-be-abandoned sync
						//either just finished correctly (and this StopSync() call will behave as if it was called too late)
						//or waits until it is properly abandoned

						if (syncRunning) //allow to stop the sync only if it is running 
						{

							//entering this scope means that the sync has not finished yet and it will be abandoned

							//if sync() finishes just before this very if, StopSync() will just exit without invalidating the results
							//which is semantically same as calling StopSync() any time after finishing the sync() and before
							//calling next StartSync()

							saveResult.BoolValue = false; //tell the about-to-be-abandoned thread not to save any information back to this class instance

							sntpClientQuery.StopCommunication(); //close the opened socket
							//this probably throws an exception in Sntpv4ClientQuery.AskServer() and returns control back to sync() which will then wait for the lock

							//clean the references so that nothing tries to use them again
							sntpClientQuery = null;
							saveResult = null;
							timeoutStopwatchKillSyncOnTimeout = null;

							syncOK = false;
							clockUpdatedOK = false;
							syncRunning = false; //from this moment on, a StartSync() call can wait for a lock and by the time it is obtained, everything is already sanitized
							//and prepared for a new sync

						}
						//the abandoned thread is allowed to exit at this point, with the BoolWrapper instance telling it to not update any values in this class instance
					}
					lock (stopSyncAlreadyCalledLocker)
					{
						stopSyncAlreadyCalled = false;
						//since we are the one who was the first and we are just terminating, note that there is no one running any longer
					}
				}
			}
		}

		private void sync(BooleanWrapper SaveResult, BooleanWrapper KillOnTimeout, Sntpv4ClientQuery SntpClientQuery)
		{

			BooleanWrapper referenceToMySaveResultBooleanWrapper = SaveResult;
			BooleanWrapper referenceToMyKillOnTimeoutBooleanWrapper = KillOnTimeout;
			Sntpv4ClientQuery referenceToMySntpClientQuery = SntpClientQuery;

			//this thread has a reference to the very same BooleanWrapper instance;
			//if this thread is abandoned, the value in this particular BooleanWrapper instance will be set to false;
			//this method tests the value of the same BooleanWrapper instance at the end and so it will correctly
			//decide whether or not to update the ntp packet in this class

			//generally in this class, each thread with sync() has its own BooleanWrapper instance and when a thread
			//is abandoned, the saveResult variable (which is a reference to the BooleanWrapper) is deleted (leaving the
			//BooleanWrapper object still alive, referenced only from the abandoned thread) and the thread will read
			//its value as soon as it unfreezes from the blocking call, be it in a few ms or in several hours


			referenceToMySntpClientQuery.AskServer();

			lock (syncLocker) //forbid others from interfering (StartSync(), StopSync(), timeout)
			{

				if (referenceToMySaveResultBooleanWrapper.BoolValue) //this thread is not abandoned
				{
					//tell the timeoutStopwatch thread to die peacefully without stopping anything,
					//by the time it wakes up from sleep, there might be another sync going on
					referenceToMyKillOnTimeoutBooleanWrapper.BoolValue = false;

					Boolean continueBool = true;

					if (referenceToMySntpClientQuery.SyncRunning)
					{ //sanity check
						continueBool = false; //if the sntpClientQuery reports it is still running, something is very bad (AskServer() returned)
					}
					if (!referenceToMySntpClientQuery.SyncOK)
					{
						continueBool = false; //if the sync was not OK, report it as not OK
					}

					if (continueBool)
					{ //if it seems OK
						ntpPacketFromSync = referenceToMySntpClientQuery.ReceivedPacket();
						if (updateSystemClockWhenSynced)
						{
							clockUpdatedOK = UpdateSystemClock();
						}
						syncOK = true;
						syncRunning = false;


						saveResult = null;
						sntpClientQuery = null;
						timeoutStopwatchKillSyncOnTimeout = null;
					}
					else
					{ //if it is not OK
						syncOK = false;
						clockUpdatedOK = false;
						syncRunning = false; //signal stopping sync BEFORE retrying
						//syncRunning will be read on retry, it is a volatile variable and so the read is assured to be fresh

						saveResult = null;
						sntpClientQuery = null;
						timeoutStopwatchKillSyncOnTimeout = null; //cleanup BEFORE retrying


						//retry sync
						retrySync(); //this retrySync() is called already under syncLocker lock
					}
				}
			}
		}

		private void timeoutStopwatch(BooleanWrapper TimeoutStopwatchKillSyncOnTimeout, int Timeout)
		{
			//this object is specific for this thread and the other thread with sync(), the reference in the class instance
			//is purged once the sync() and/or timeoutStopwatch() are abandoned, leaving the object referenced only
			//by sync() and/or timeoutStopwatch(), thus creating an isolated communication channel totally independent on
			//the class instance itself and other threads.
			BooleanWrapper referenceToMyTimeoutStopwatchKillSyncOnTimeoutBooleanWrapper = TimeoutStopwatchKillSyncOnTimeout;

			int timeout = Timeout;
			Thread.Sleep(timeout); //sleep until the sync() has to be stopped and only then find out whether to stop it or not
			//sync() either already finished (this method will exit then) or it is still stuck (it will be told to die, then)
			lock (syncLocker)
			{
				if (referenceToMyTimeoutStopwatchKillSyncOnTimeoutBooleanWrapper.BoolValue)
				{ //stop sync() IF we are allowed to do so
					//StopSync() locks syncLocker; since StopSync() will be called on this thread which already has locked syncLocker
					//and the lock is reentrant within a thread, StopSync() call is perfectly valid and will not block itself.
					//Locking the lock twice ensures that there is no possible point of change between reading the boolean value
					//and calling StopSync(), thus ensuring the correct sync() is being abandoned.
					StopSync();

					//retry sync
					retrySync(); //this retrySync() is called already under syncLocker lock
				}
				//after releasing this lock, sync() is allowed to lock it and discover it should die, which has been set by StopSync()
			}
		}

		private void retrySync()
		{
			//the global variables such as millisecondsBetweenRetries and retriesFromFailure are volatile so that the read value is always current 
			lock (syncLocker)
			{ //lock to make verification of correctness easier; lock is reentrant and poses no problem if retrySync() is called from
				//the correct place in sync() or timeoutStopwatch() and also poses no problem when calling StartSync() from within retrySync()

				if (numOfRetriesPerformed < retriesFromFailure)
				{
					//wait
					//We are in a thread started for either sync() or timeoutStopwatch() and thus we do not block the main thread.
					//We can sleep safely.
					Thread.Sleep(millisecondsBetweenRetries);

					//increase the counter
					numOfRetriesPerformed += 1;

					StartSync();
				}
				else
				{ //if (numOfRetriesPerformed >= retriesFromFailure)
					//the number of performed retries is up
					//reset the counter and stop trying
					numOfRetriesPerformed = 0;
				}
			}
		}
		private Boolean UpdateSystemClock()
		{
			return Helper.CorrectOSClockImprecise(ntpPacketFromSync.SystemClockOffset);
		}

	}
}

