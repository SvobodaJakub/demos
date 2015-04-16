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
	public class Sntpv4Server
	{
		public System.Diagnostics.Stopwatch LastActionStopwatch = null;
		int port = 123;

		//This lock always locks access to mainServerThread and mainServerThreadKeepAlive, making sure concurrent StartServer() and StopServer() invocations don’t result in a race condition.
		//Locks access to:
		// * mainServerThread (read and write access)
		// * mainServerThreadKeepAlive (write access)
		private readonly Object mainServerThreadLocker = new Object();
		private Thread mainServerThread;
		private volatile BooleanWrapper mainServerThreadKeepAlive;
		//mainServerThreadKeepAlive is assigned a new BooleanWrapper instance for 

		private volatile Boolean serverRunning = false;
		private readonly Object serverRunningLocker = new Object(); //locks all access to serverRunning
		private readonly Object startStopLocker = new Object(); //locked in StartSync and StopSync

		private long localClockOffsetTicks = 0;
		private readonly Object localClockOffsetTicksLocker = new Object();
		public long LocalClockOffsetTicks
		{
			get
			{
				lock (localClockOffsetTicksLocker)
				{
					return localClockOffsetTicks;
				}
			}

			set
			{
				lock (localClockOffsetTicksLocker)
				{
					localClockOffsetTicks = value;
					referenceTimestampOnOffsetChange = new Timestamp(Helper.DateTimeToNtpTicks(Helper.TicksToDateTime(Helper.CurrentUtcDateTime().Ticks + localClockOffsetTicks)));
				}
			}
		}

		private Timestamp referenceTimestampOnOffsetChange = new Timestamp(0);


		/// <summary>
		/// The time is sent only if true.
		/// </summary>
		public volatile Boolean OkayToRun = false; //the time is sent only if true

		public Sntpv4Server()
		{
			//port 123 and unlimited num of clients
		}

		public Sntpv4Server(int ListeningPort)
		{
			port = ListeningPort;
		}

		public void StartServer()
		{
			if (!serverRunning)
			{
				lock (startStopLocker)
				{
					lock (serverRunningLocker)
					{
						if (!serverRunning)
						{ //double-checked locking 

							serverRunning = true;

							BooleanWrapper keepAlive = new BooleanWrapper(true); //creating a local copy [of reference type variable] so that the thread will be spawned correctly, no matter how delayed
							mainServerThreadKeepAlive = keepAlive;

							Thread serverThread = new Thread(
								delegate()
								{
								try
								{
									listeningServer(keepAlive);
								}
								catch
								{
									lock (mainServerThreadLocker)
									{
										//there may have been an error in the server (apart from the thread being aborted)
										//find out whether this thread was set to die
										if (keepAlive.BoolValue)
										{
											//and continue only if it was not set to die
											//that means the server died unexpectedly
											//sanitize the environment so that it looks like it was never started (and a new one can be started)
											mainServerThread = null;
											mainServerThreadKeepAlive = null;
										}
										// else it was set to die anyway so the environment was sanitized at that point and it would be
										// dangerous to interfere with the environment since a new server may have already been started
									}
								}
								finally
								{
									//sanitize
									lock (serverRunningLocker)
									{
										serverRunning = false;
									}
								}
							}
							);
							serverThread.IsBackground = true; //kill the thread when the app exits
							serverThread.Start();
							//myThreads.Add(timerThread);
							mainServerThread = serverThread;
						}
					}
				}
			}
		}

		public void StopServer()
		{
			lock (startStopLocker)
			{
				Thread threadToKill = null;
				if (serverRunning)
				{
					lock (serverRunningLocker)
					{
						if (serverRunning)
						{ //double-checked locking 

							mainServerThreadKeepAlive.BoolValue = false; //this is superfluous as long as we perform Abort and Join immediately on this thread

							threadToKill = mainServerThread;
							//mainServerThread.Abort(); //this produces an exception in the thread

							//contents of mainServerThread and mainServerThreadKeepAlive are already saved into a local variable that will exit only as long as the killing thread exists
							//clear the global [reference type] variable, so that a server can be started anew
							mainServerThread = null;
							mainServerThreadKeepAlive = null;

						}
					}
				}
				if (threadToKill != null)
				{

					//close the socket that otherwise blocks, rendering the thread unabortable
					try
					{
						listeningServerSocket.Shutdown(SocketShutdown.Both);
					}
					catch { }
					try
					{
						listeningServerSocket.Close();
					}
					catch { }

					Thread.Sleep(3000); //wait for the socket to shut down
					//experiments showed the socket does not close immediately and opening a new one too soon results in a total crash of mono :-( (version 2.10.8.1 (Debian 2.10.8.1-5) on soft-float ARMv6 (raspi))

					threadToKill.Abort();

					threadToKill.Join(); //wait for it to die so that the socket is closed and thread aborted before this method returns
					//joining outside of the lock so that the thread can process its catch
				}

			}
		}

		private Socket listeningServerSocket;
		private void listeningServer(BooleanWrapper keepAlive)
		{
			BooleanWrapper myKeepAlive = keepAlive;
			while (myKeepAlive.BoolValue)
			{ //restart the socket until the server is stopped
				if (OkayToRun) //run only if it is OK to run
				{
					Thread.Sleep(150);

					Byte[] receivedNtpPacketBytes = new Byte[48];

					IPEndPoint sntpServerIPEndPoint = new IPEndPoint(IPAddress.Any, port); //IPAddress.Any means that no network interface preference is being set (applicable when the machine has more network interfaces)
					Socket listeningSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp); //IPv4, UDP datagram
					listeningSocket.ReceiveTimeout = 10 * Constants.NetworkTimeoutMs;
					listeningSocket.SendTimeout = Constants.NetworkTimeoutMs;
					listeningServerSocket = listeningSocket;
					//when the timeout is set to 1 ms, the error rate is around 9% on a loopback connection

					listeningSocket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, 1);
					//http://www.dotnet247.com/247reference/msgs/32/164271.aspx
					//http://stackoverflow.com/questions/2763933/what-is-socketoptionname-reuseaddress-used-for

					try
					{
						listeningSocket.Bind(sntpServerIPEndPoint); //associates listeningSocket with sntpServerIPEndPoint
						while (myKeepAlive.BoolValue)
						{//process clients one by one

							if (OkayToRun)//run only if it is OK to run
							{
								IPEndPoint sntpClientIPEndPoint = new IPEndPoint(IPAddress.Any, 0); //prepare the variable to obtain the (S)NTP sync address
								EndPoint sntpClientEndPoint = (EndPoint)sntpClientIPEndPoint; //(S)NTP sync address

								listeningSocket.ReceiveFrom(receivedNtpPacketBytes, ref sntpClientEndPoint);

								Timestamp receiveTimestamp = new Timestamp(Helper.DateTimeToNtpTicks(Helper.TicksToDateTime(Helper.CurrentUtcDateTime().Ticks + LocalClockOffsetTicks)));

								NtpPacket receivedPacket = new NtpPacket();
								receivedPacket.WholePacketByteArray = receivedNtpPacketBytes;

								NtpPacket replyPacket = computeNtpPacketReply(receivedPacket, receiveTimestamp); //compute the reply so that the sync can compute the correct time
								Byte[] replyNtpPacketBytes = replyPacket.WholePacketByteArray;

								listeningSocket.SendTo(replyNtpPacketBytes, replyNtpPacketBytes.Length, SocketFlags.None, sntpClientEndPoint); //send a reply to the sync

								LastActionStopwatch = new System.Diagnostics.Stopwatch();
								LastActionStopwatch.Start();
							}
							else //if it is not OK to run, wait and then try again
							{
								Thread.Sleep(1000);
							}
						}
					}
					catch
					{
					}
					finally
					{
						//the socket must be properly shut down and closed no matter what (for it to be still available in the current application run)
						try
						{
							listeningSocket.Shutdown(SocketShutdown.Both);
						}
						catch { }
						try
						{
							listeningSocket.Close();
						}
						catch { }
						listeningSocket = null;
					}
				}
				else //if it is not OK to run, wait and then try again
				{
					Thread.Sleep(1000);
				}
			}
		}

		private NtpPacket computeNtpPacketReply(NtpPacket ReceivedPacket, Timestamp ReceiveTimestamp)
		{
			NtpPacket reply = ReceivedPacket.DeepClone();

			//When the server reply is received, the sync determines a
			//Destination Timestamp variable as the time of arrival according to
			//its clock in NTP timestamp format.  The following table summarizes
			//the four timestamps.

			//   Timestamp Name          ID   When Generated
			//   ------------------------------------------------------------
			//   Originate Timestamp     T1   time request sent by sync
			//   Receive Timestamp       T2   time request received by server
			//   Transmit Timestamp      T3   time reply sent by server
			//   Destination Timestamp   T4   time reply received by sync

			//The roundtrip delay d and system clock offset t are defined as:

			//   d = (T4 - T1) - (T3 - T2)     t = ((T2 - T1) + (T3 - T4)) / 2.

			//Transmit->Originate
			reply.OriginateTimestamp = reply.TransmitTimestamp;

			//time request received by server
			reply.ReceiveTimestamp = ReceiveTimestamp;

			reply.ReferenceTimestamp = referenceTimestampOnOffsetChange;

			reply.Stratum = 1; //TimeSync3 is directly connected to EDHau
			reply.LI = 0; //it does not make sense to detect leap seconds; the error the static zero incurs is nearly nonexistent
			reply.Mode = 4; //server reply
			PrecisionStruct precision = new PrecisionStruct();
			precision.PrecisionValueSeconds = 0.01;
			reply.Precision = precision;
			RootDelayStruct rootDelay = new RootDelayStruct();
			rootDelay.RootDelayValueSeconds = 0; //this value is 0 for Stratum 1
			reply.RootDelay = rootDelay;
			RootDispersionStruct rootDispersion = new RootDispersionStruct();
			rootDispersion.RootDispersionValueSeconds = 0; //this value is 0 for Stratum 1
			reply.RootDispersion = rootDispersion;

			//time source is DCF
			Byte[] referenceIdentifier4Bytes = new Byte[4];
			referenceIdentifier4Bytes[0] = (Byte)'D';
			referenceIdentifier4Bytes[1] = (Byte)'C';
			referenceIdentifier4Bytes[2] = (Byte)'F';
			referenceIdentifier4Bytes[3] = 0x00;
			reply.ReferenceIdentifierByteArray = referenceIdentifier4Bytes;

			PollStruct poll = new PollStruct();
			poll.IntervalSeconds = 1;
			reply.Poll = poll;

			//time reply sent by server
			Timestamp transmitTimestamp = new Timestamp(Helper.DateTimeToNtpTicks(Helper.TicksToDateTime(Helper.CurrentUtcDateTime().Ticks + LocalClockOffsetTicks)));
			reply.TransmitTimestamp = transmitTimestamp;


			return reply;
		}

	}
}

