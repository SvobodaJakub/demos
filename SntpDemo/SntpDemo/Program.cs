using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace SntpDemo
{
    class Program
    {
        
        static void Main(string[] args)
        {


			System.Console.WriteLine ("Demo 1 - SNTP client");

			// create a new sntp client and perform sync once
			// please note that the Sntpv4Client can be used for as many queries as necessary
			// and has both synchronous and asynchronous methods
			Sntpv4Client sntpClient = new Sntpv4Client ("ntp.ubuntu.com");
			sntpClient.StartSyncAndBlockUntilCompleted ();

			System.Console.WriteLine (sntpClient.SyncOK ? "Sync OK" : "Sync error");
			if (sntpClient.SyncOK) {
				System.Console.WriteLine ("System clock offset: " + Helper.TicksToTimeSpan (sntpClient.NtpPacketFromSync.SystemClockOffset).ToString ());
				System.Console.WriteLine ("(System clock + offset = NTP server's time)");
			}
			System.Console.WriteLine ();


			System.Console.WriteLine ("Demo 2 - SNTP server");
			System.Console.WriteLine (
				"Shut down any NTP/SNTP server you may have running and run this program as root/\n" +
				"administrator. Try to use it as an SNTP server. The system clock is used as\n" +
				"the time source. You may need to modify computeNtpPacketReply() and use the\n" +
				"correct stratum, source identifier, and precision value based on your\n" +
				"application. Currently, DCF time source is reported, since this is the\n" +
				"application I used it for.\n" +
				"You can use ntpdate -d 127.0.0.1 if you want to test it on your machine.");
			System.Console.WriteLine ();


			Sntpv4Server sntpServer = new Sntpv4Server ();

			// remember, that Sntpv4Server remembers the last time this property was set and reports the information to the clients
			// the original usecase requires the sntp server to have fresh clock offset information available (synced from DCF)
			sntpServer.LocalClockOffsetTicks = 0; 

			// the original usecase allows the sntp server to run only after fresh time was fetched from DCF after startup
			sntpServer.OkayToRun = true;

			// runs asynchronously
			sntpServer.StartServer ();


			System.Console.WriteLine ("Press Enter to quit.");
			System.Console.ReadLine ();
			System.Console.WriteLine ("Stopping the server...");
			sntpServer.StopServer ();
			System.Console.WriteLine ("Done.");

        }
    }
}
