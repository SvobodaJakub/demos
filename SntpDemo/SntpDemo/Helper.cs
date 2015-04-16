using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Net;
using System.Xml;

namespace SntpDemo
{

	/// <summary>
	/// Various helper functions.
	/// </summary>
    static class Helper
    {



        public static void RunCommandShellExecute(string filename, string arguments)
        {
            //note: this method can throw an exception
            System.Diagnostics.ProcessStartInfo processStartInfo = new System.Diagnostics.ProcessStartInfo();

            processStartInfo.FileName = filename;
            processStartInfo.Arguments = arguments;
            processStartInfo.UseShellExecute = false;
            System.Diagnostics.Process proc = new System.Diagnostics.Process();
            proc.StartInfo = processStartInfo;
            proc.Start();

            proc.WaitForExit();

        }

        public static String RunCommand(string filename, string arguments)
        {
            //note: this method can throw an exception
            string output = "";
            System.Diagnostics.ProcessStartInfo processStartInfo = new System.Diagnostics.ProcessStartInfo();

            processStartInfo.FileName = filename;
            processStartInfo.Arguments = arguments;
            processStartInfo.RedirectStandardOutput = true;
            processStartInfo.UseShellExecute = false;
            System.Diagnostics.Process proc = new System.Diagnostics.Process();
            proc.StartInfo = processStartInfo;
            proc.Start();

            output = proc.StandardOutput.ReadToEnd();

            proc.WaitForExit();
            return output;
        }

        /// <summary>
        /// Returns null if the input string is not an IP address (e.g. a command injection attack attempt) or the original input string if it is an IP address.
        /// Dotted decimal (192.168..1) or decimal (3232235521) formats are not supported.
        /// </summary>
        /// <param name="potentiallyInvalidIpAddress"></param>
        /// <returns></returns>
        public static String ValidatedIpAddress(String potentiallyInvalidIpAddress)
        {

            //length check
            if (potentiallyInvalidIpAddress.Length > (3 + 1 + 3 + 1 + 3 + 1 + 3))
            {
                //string too long
                return null;
            }
            if (potentiallyInvalidIpAddress.Length < (1 + 1 + 1 + 1 + 1 + 1 + 1))
            {
                //string too short
                return null;
            }

            //parse the string using the dots
            String[] parts = potentiallyInvalidIpAddress.Split('.');

            if (parts.Length != 4)
            {
                return null; // the IP address doesn't consist of 4 numbers
            }

            for (int i = 0; i < parts.Length; i++)
            {
                //check each part that it is a number between 0 and 255
                try
                {
                    int num = Int32.Parse(parts[i]);
                    if (num < 0)
                    {
                        return null; //below 0
                    }
                    if (num > 255)
                    {
                        return null; //above 255
                    }
                }
                catch
                {
                    return null; //not a number
                }
            }

            //if it passed the check, it’s okay
            return potentiallyInvalidIpAddress;

        }
        /// <summary>
        /// Immediately configures IP address, mask and gateway. In order to not allow a command injection attack, the addresses are validated.
        /// </summary>
        /// <param name="ipAddress"></param>
        /// <param name="mask"></param>
        /// <param name="gateway"></param>
        public static void ApplyOSIPConfiguration(String ipAddress, String mask, String gateway)
        {
            //ifconfig eth0 down
            //ifconfig eth0 10.0.2.12 netmask 255.255.255.0 up
            //route add default gw 10.0.2.1 eth0

            String validatedAddr = ValidatedIpAddress(ipAddress);
            String validatedMask = ValidatedIpAddress(mask);
            String validatedGw = ValidatedIpAddress(gateway);

            if (validatedAddr == null)
            {
                throw new NullReferenceException();
            }
            if (validatedMask == null)
            {
                throw new NullReferenceException();
            }
            if (validatedGw == null)
            {
                throw new NullReferenceException();
            }

            //write the configuration to the file
            try
            {
                System.IO.File.Delete("/etc/network/interfaces");
            }
            catch { }
            try
            {
                System.IO.TextWriter interfacesFile = System.IO.File.CreateText("/etc/network/interfaces");
                interfacesFile.WriteLine("auto lo");
                interfacesFile.WriteLine("");
                interfacesFile.WriteLine("iface lo inet loopback");
                interfacesFile.WriteLine("iface eth0 inet static");
                interfacesFile.WriteLine("  address " + validatedAddr);
                interfacesFile.WriteLine("  netmask " + validatedMask);
                interfacesFile.WriteLine("  gateway " + validatedGw);
                interfacesFile.WriteLine("");
                interfacesFile.Close();
            }
            catch { }

            //apply the configuration immediately
            String ifconfigUp = "ifconfig eth0 " + ipAddress + " netmask " + mask + " up";
            String routeGw = "route add default gw " + gateway + " eth0";
            try
            {
                Helper.RunCommandShellExecute("bash", " -c \"ifconfig eth0 down\"");
                System.Threading.Thread.Sleep(300);
                Helper.RunCommandShellExecute("bash", " -c \"" + ifconfigUp + "\"");
                System.Threading.Thread.Sleep(1300);
                Helper.RunCommandShellExecute("bash", " -c \"" + routeGw + "\"");
                System.Threading.Thread.Sleep(100);
            }
            catch { }


        }

        /// <summary>
        /// Enables or disables sshd in Linux. Calls /etc/init.d/ssh start|stop.
        /// </summary>
        /// <param name="enabled">Enable if true, disable if false.</param>
        public static void ToggleSSHd(Boolean enabled)
        {
            try
            {
                String command = "/etc/init.d/ssh " + (enabled ? "start" : "stop");
                Helper.RunCommandShellExecute("bash", " -c \"" + command + "\"");
            }
            catch { }
        }

        /// <summary>
        /// Remounts / partition as read only.
        /// </summary>
        public static void RemountRO()
        {
            try
            {
                String command = "sync";
                Helper.RunCommandShellExecute("bash", " -c \"" + command + "\"");
            }
            catch { }
            try
            {
                String command = "mount -o remount,ro /";
                Helper.RunCommandShellExecute("bash", " -c \"" + command + "\"");
            }
            catch { }
        }

        /// <summary>
        /// Remounts / partition as writable.
        /// </summary>
        public static void RemountRW()
        {
            try
            {
                String command = "mount -o remount,rw /";
                Helper.RunCommandShellExecute("bash", " -c \"" + command + "\"");
            }
            catch { }
        }

        /// <summary>
        /// Adjusts OS clock based on the offset. If the offset is -100000, it means the clock is 100000 ticks ahead and the clock will be adjusted by subtracting 100000 ticks from the current time.
        /// The way of setting the OS clock used in this method is imprecise. This is not an issue for the embedded appliance usecase. This is an issue for the Windows service usecase.
        /// </summary>
        /// <param name="LocalClockOffsetTicks">How many ticks to add to the OS clock to adjust it.</param>
        /// <returns></returns>
        public static Boolean CorrectOSClockImprecise(long LocalClockOffsetTicks)
        {
            try
            {
                DateTime timeCurrent = Helper.CurrentLocalDateTime();
                DateTime time = timeCurrent.AddTicks(LocalClockOffsetTicks);
                int hour = time.Hour;
                int minute = time.Minute;
                int second = time.Second;
                int year = time.Year;
                int month = time.Month;
                int day = time.Day;
                String timeString = hour.ToString() + ":" + minute.ToString() + ":" + second.ToString();
                String dateString = day.ToString() + "." + month.ToString() + "." + year.ToString();
                String linuxString = year.ToString() + "-" + month.ToString() + "-" + day.ToString() + " " + hour.ToString() + ":" + minute.ToString() + ":" + second.ToString();


                if ((Environment.OSVersion.Platform == PlatformID.Win32NT) || (Environment.OSVersion.Platform == PlatformID.Win32Windows))
                {
                    //assume windows
                    Helper.RunCommandShellExecute("cmd", "/C time " + timeString);

                    Helper.RunCommandShellExecute("cmd", "/C date " + dateString);

                    return true;
                }
                else
                {
                    if (year < 2038) //year 2038 problem on 32-bit unix systems
                    {
                        //assume linux
                        // " -c \"echo hello;#;#;#\"")
                        //# date -s "2012-1-3 12:3:2" 
                        //Tue Jan  3 12:03:02 UTC 2012     
                        Helper.RunCommandShellExecute("bash", " -c \"date -s \\\"" + linuxString + "\\\"\"");
                        return true;
                    }
                    else
                    {
                        return false;
                    }
                }
            }
            catch
            {
                return false;
            }

        }

        public static IPEndPoint FindServer(String ServerHostname, int ServerPort)
        {
            System.Net.IPAddress serverAddress;
            if (System.Net.IPAddress.TryParse(ServerHostname, out serverAddress))
            {
                //It is an IP address!
                //do nothing
            }
            else
            {
                //It is a host name
                System.Net.IPAddress[] addressList = Dns.GetHostEntry(ServerHostname).AddressList;
                serverAddress = addressList[0];
            }
            System.Net.IPEndPoint serverIpEndPoint = new IPEndPoint(serverAddress, ServerPort);
            return serverIpEndPoint;
        }

        public static DateTime TicksToDateTime(long Ticks)
        {
            DateTime dateTime = new DateTime(Ticks);
            return dateTime;
        }

        public static long DateTimeToTicks(DateTime dateTime)
        {
            return dateTime.Ticks;
        }

        public static TimeSpan TicksToTimeSpan(long Ticks)
        {
            TimeSpan timeSpan = new TimeSpan(Ticks);
            return timeSpan;
        }
        public static DateTime NtpBeginningOfTime(Boolean Bit0)
        {
            //As the NTP timestamp format has been in use for over 20 years, it
            //is possible that it will be in use 32 years from now, when the
            //seconds field overflows.  As it is probably inappropriate to
            //archive NTP timestamps before bit 0 was set in 1968, a convenient
            //way to extend the useful life of NTP timestamps is the following
            //convention: If bit 0 is set, the UTC time is in the range 1968-
            //2036, and UTC time is reckoned from 0h 0m 0s UTC on 1 January
            //1900.  If bit 0 is not set, the time is in the range 2036-2104 and
            //UTC time is reckoned from 6h 28m 16s UTC on 7 February 2036.  Note
            //that when calculating the correspondence, 2000 is a leap year, and
            //leap seconds are not included in the reckoning.

            //The arithmetic calculations used by NTP to determine the clock
            //offset and roundtrip delay require the sync time to be within 34
            //years of the server time before the sync is launched.  As the
            //time since the Unix base 1970 is now more than 34 years, means
            //must be available to initialize the clock at a date closer to the
            //present, either with a time-of-year (TOY) chip or from firmware.

            if (Bit0)
            { //bit 0 is set
                return new DateTime(1900, 1, 1);
            }
            else
            { //bit 0 is not set
                return new DateTime(2036, 2, 7, 6, 28, 16);
            }
        }
        public static DateTime NtpTicksToDateTime(long Ticks, Boolean Bit0)
        {
            return NtpBeginningOfTime(Bit0) + TicksToTimeSpan(Ticks);
        }
        public static DateTime NtpTimestampToDateTime(Timestamp timestamp)
        {
            return NtpBeginningOfTime(Bit0FromSeconds(timestamp.Seconds)) + TicksToTimeSpan(timestamp.Ticks);
        }
        public static Boolean Bit0FromDateTime(DateTime dateTime)
        {
            if (DateTimeToTicks(dateTime) > DateTimeToTicks(NtpBeginningOfTime(false)))
            {
                return false;
            }
            else
            {
                return true;
            }
        }

        public static Boolean Bit0FromSeconds(long Seconds)
        {
            return (Seconds >= 0x80000000); //bit 0 has the value of 2^31 and is set if the number is >= 2^31
        }
        public static long DateTimeToNtpTicks(DateTime dateTime)
        {
            Boolean bit0 = Bit0FromDateTime(dateTime);
            return (DateTimeToTicks(dateTime) - DateTimeToTicks(NtpBeginningOfTime(bit0)));
        }
        public static DateTime CurrentUtcDateTime()
        {
            return DateTime.UtcNow;
        }

        public static DateTime CurrentLocalDateTime()
        {
            return DateTime.Now;
        }

        /// <summary>
        /// Offset between current time zone and UTC.
        /// </summary>
        /// <returns></returns>
        public static TimeSpan LocalClockOffset()
        {
            TimeSpan utcOffset = System.TimeZone.CurrentTimeZone.GetUtcOffset(DateTime.Now);
            return utcOffset;
        }
        public static Byte[] SwapEndianness4Bytes(Byte[] Array)
        {
            Byte[] swappedArray = new Byte[4];
            for (int i = 0; i < 4; i++)
            {
                swappedArray[i] = Array[3 - i];
            }
            return swappedArray;
        }
    }
}
