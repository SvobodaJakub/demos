using System;

namespace SntpDemo
{

	/// <summary>
	/// Class containing one thread-safe volatile boolean property. Can be used for signalling between threads.
	/// </summary>
	public class BooleanWrapper
	{
		//Aim of this class is to allow passing a boolean value across otherwise unconnected objects.
		//Its parallel in the real world could be quantum entanglement (exploited in such a way that transmission of information is possible, which is sadly not possible as of 2013).
		//What is the reason this class exists? Imagine the following scenario:
		//Object A creates a thread B that can change a field F in the Object A if everything is OK. (In this app, it is time synchronization.)
		//If something goes wrong, we want to abandon the thread B and (without waiting) start over with a new thread C that tries to do the same as the thread B.
		//However, the mechanism to update the field F in the Object A requires one of the following: Another controlling thread X that checks whether
		//to ignore the result or save the result to the field F OR the thread B saving the value into F itself.
		//The former is too heavy-weight and the latter has a problem of how to pass the information to not update F.
		//Aborting the thread B using Thread.Abort() is not an option because it can cause all sorts of problems and does not solve the aforementioned problem.
		//The BooleanWrapper class makes it possible for the following scenario to work:
		//Object A creates a thread B. Object A and the thread B both have a reference to an instance W of BooleanWrapper telling thread B whether or
		//not to update the field F in A. Thread B  happens to hang during a blocking call (ignore for a moment that it is possible to set timeouts on some
		//blocking calls) and killing it is not an option since that particular blocking call happens outside of the CLR. Object A changes value inside
		//the shared BooleanWrapper W and abandons the thread B. At some point in the future, the blocking call timeouts or returns finally and the
		//abandoned thread B only then reads the value from W and decides not to update the field F. Meanwhile, A can delete its references to thread
		//B and BooleanWrapper W and create a new thread C and a new BooleanWrapper W2 and try again (In this application, try the timesync again.).
		//At no point should A worry that an abandoned thread rewrites F with old data and there are no controlling threads & events needed.
		private volatile Boolean boolValue;
		private readonly Object boolValueLocker = new Object();
		public Boolean BoolValue
		{
			get
			{
				lock (boolValueLocker)
				{
					return boolValue;
				}
			}
			set
			{
				lock (boolValueLocker)
				{
					boolValue = value;
				}
			}
		}
		public BooleanWrapper(Boolean Value)
		{
			BoolValue = Value;
		}
	}
}

