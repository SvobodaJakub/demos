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
	public class Sntpv4ClientQuery
	{
		private String ntpServer;
		private int ntpPort = 123;
		private NtpPacket ntpPacketToBeSent = new NtpPacket();
		private NtpPacket ntpPacketReceived = new NtpPacket(); //some methods will report bogus data until SyncStopped and SyncOK are true but will not crash
		private volatile Boolean syncRunning; //true if sync is in progress
		private volatile Boolean syncOK; //true if the last sync with server was OK (false otherwise or if no sync occurred at all)
		public Boolean SyncRunning
		{ //true if sync is in progress
			get
			{
				return syncRunning;
			}
		}
		public Boolean SyncOK //true if the last sync with server was OK (false otherwise or if no sync occurred at all)
		{
			get
			{
				return syncOK;
			}
		}
		private Socket openedSocket;
		public int ReceiveTimeout { get; set; }
		private readonly Object syncInProgressLocker = new Object(); //lock to limit the instance of this class to one sync at a time

		public Sntpv4ClientQuery(String NtpServer)
		{
			ntpServer = NtpServer;
		}

		public Sntpv4ClientQuery(String NtpServer, int NtpPort)
		{
			ntpServer = NtpServer;
			ntpPort = NtpPort;
		}

		private void initializeProperties()
		{
			syncRunning = false;
			syncOK = false;
		}

		private void InitializePacket()
		{

			//   Field Name               Unicast/Manycast            Broadcast
			//                            Request     Reply
			//   ---------------------------------------------------------------
			//   LI                       0           0-3            0-3

			//   VN                       1-4         copied from    1-4
			//                                        request

			//   Mode                     3           4              5

			//   Stratum                  0           0-15           0-15

			//   Poll                     0           ignore         ignore

			//   Precision                0           ignore         ignore

			//   Root Delay               0           ignore         ignore

			//   Root Dispersion          0           ignore         ignore

			//   Reference Identifier     0           ignore         ignore

			//   Reference Timestamp      0           ignore         ignore

			//   Originate Timestamp      0           (see text)     ignore

			//   Receive Timestamp        0           (see text)     ignore

			//   Transmit Timestamp       (see text)  nonzero        nonzero

			//   Authenticator            optional    optional       optional

			//Although not required in a conforming SNTP sync implementation, it
			//is wise to consider a suite of sanity checks designed to avoid
			//various kinds of abuse that might happen as the result of server
			//implementation errors or malicious attack.  Following is a list of
			//suggested checks.

			//1.  When the IP source and destination addresses are available for
			//    the sync request, they should match the interchanged addresses
			//    in the server reply.

			//2.  When the UDP source and destination ports are available for the
			//    sync request, they should match the interchanged ports in the
			//    server reply.

			//3.  The Originate Timestamp in the server reply should match the
			//    Transmit Timestamp used in the sync request.

			//4.  The server reply should be discarded if any of the LI, Stratum,
			//    or Transmit Timestamp fields is 0 or the Mode field is not 4
			//    (unicast) or 5 (broadcast).

			//5.  A truly paranoid sync can check that the Root Delay and Root
			//    Dispersion fields are each greater than or equal to 0 and less
			//    than infinity, where infinity is currently a cozy number like one
			//    second.  This check avoids using a server whose synchronization
			//    source has expired for a very long time.

			//Completely initialize the ntp packet even though it initializes itself in the constructor
			Byte[] blank48 = new Byte[48];
			for (int i = 0; i < 48; i++)
			{
				blank48[i] = 0x00;
			}
			ntpPacketToBeSent.WholePacketByteArray = blank48;
			ntpPacketToBeSent.LI = 0;
			ntpPacketToBeSent.VN = 3; //SNTPv4; this may be lowered to 3 to ensure compatibility with v3 servers
			ntpPacketToBeSent.Mode = 3; //sync
			ntpPacketToBeSent.Stratum = 0;
			PollStruct poll = new PollStruct();
			poll.ValueInPacket = 0;
			ntpPacketToBeSent.Poll = poll;
			PrecisionStruct precision = new PrecisionStruct();
			precision.ValueInPacket = 0;
			ntpPacketToBeSent.Precision = precision;
			RootDelayStruct rootDelay = new RootDelayStruct();
			rootDelay.ValueInPacket = 0;
			ntpPacketToBeSent.RootDelay = rootDelay;
			RootDispersionStruct rootDispersion = new RootDispersionStruct();
			rootDispersion.ValueInPacket = 0;
			ntpPacketToBeSent.RootDispersion = rootDispersion;
			ntpPacketToBeSent.ReferenceIdentifierLong = 0;
			Timestamp zeroTimestamp = new Timestamp(new BitArray(new Byte[] { 0, 0, 0, 0, 0, 0, 0, 0 }));
			ntpPacketToBeSent.ReferenceTimestamp = zeroTimestamp;
			ntpPacketToBeSent.OriginateTimestamp = zeroTimestamp;
			ntpPacketToBeSent.ReceiveTimestamp = zeroTimestamp;

			ntpPacketToBeSent.TransmitTimestamp = new Timestamp(Helper.DateTimeToNtpTicks(Helper.CurrentUtcDateTime()));
		}


		public void AskServer()
		{
			lock (syncInProgressLocker) //allow only one sync at a time for the current instance
			{
				syncRunning = true;
				syncOK = false; //invalidate the last sync status since we’ve already begun with a new one
				try
				{
					System.Net.IPEndPoint ntpServerIpEndPoint = Helper.FindServer(ntpServer, ntpPort);
					openedSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp); //IPv4, UDP datagram


					Byte[] receivedBytes = new Byte[48];
					InitializePacket();
					openedSocket.ReceiveTimeout = ReceiveTimeout; //lower the timeout so that it is easier to kill the connection if something goes wrong

					Timestamp destinationTimestamp;
					try
					{
						openedSocket.Connect(ntpServerIpEndPoint);
						openedSocket.Send(ntpPacketToBeSent.WholePacketByteArray);
						openedSocket.Receive(receivedBytes);
						destinationTimestamp = new Timestamp(Helper.DateTimeToNtpTicks(Helper.CurrentUtcDateTime())); //Destination timestamp, take it ASAP after receiving the reply
					}
					finally
					{
						openedSocket.Close();
					}

					NtpPacket receivedPacket = new NtpPacket();
					receivedPacket.WholePacketByteArray = receivedBytes;
					receivedPacket.DestinationTimestamp = destinationTimestamp;
					ntpPacketReceived = receivedPacket;

					syncOK = true;
				}
				catch
				{
					//an error occurred 
					syncOK = false;
				}
				finally
				{
					syncRunning = false;
				}
			}
		}

		public void StopCommunication()
		{
			try
			{
				if (syncRunning) //allow stopping only if there is something to stop
				{
					openedSocket.Close(); //this makes the openedSocket.Receive() to throw an exception which will be contained within AskServer()
					openedSocket.Dispose(); //this is duplicit for the current version of .NET but it does no harm
				}
			}
			catch { }
		}
		public DateTime ReceivedPacketTransmitTimestamp()
		{
			Boolean bit0 = Helper.Bit0FromSeconds(ntpPacketReceived.TransmitTimestamp.Seconds);
			return Helper.NtpTicksToDateTime(ntpPacketReceived.TransmitTimestamp.Ticks, bit0);
		}

		public TimeSpan ReceivedPacketOffset()
		{
			return Helper.TicksToTimeSpan(ntpPacketReceived.SystemClockOffset);
		}

		public NtpPacket ReceivedPacket()
		{
			return ntpPacketReceived.DeepClone();
		}


	}
}

