using System;

namespace SntpDemo
{
	public struct PollStruct
	{
		public int ValueInPacket { get; set; } //the value stored in the packet
		public int IntervalSeconds //interpreted value of the Poll field
		{
			//Poll Interval: This is an eight-bit unsigned integer used as an
			//exponent of two, where the resulting value is the maximum interval
			//between successive messages in seconds.  This field is significant
			//only in SNTP server messages, where the values range from 4 (16 s) to
			//17 (131,072 s -- about 36 h).
			get
			{
				if (ValueInPacket < 4)
				{
					return 16;
				}
				else
				{
					if (ValueInPacket > 17)
					{
						return 131072;
					}
					else
					{
						return (int)Math.Pow(2, (Double)ValueInPacket);
					}
				}
			}
			set
			{
				int poll = (int)Math.Log(value, 2);
				if (poll < 4) poll = 4;
				if (poll > 17) poll = 17;
				ValueInPacket = poll;
			}
		}
	}
}

