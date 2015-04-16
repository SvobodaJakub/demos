using System;

namespace SntpDemo
{
	public struct PrecisionStruct
	{
		public int ValueInPacket { get; set; }
		public Double PrecisionValueSeconds
		{
			//Precision: This is an eight-bit signed integer used as an exponent of
			//two, where the resulting value is the precision of the system clock
			//in seconds.  This field is significant only in server messages, where
			//the values range from -6 for mains-frequency clocks to -20 for
			//microsecond clocks found in some workstations.
			get
			{
				return (Double)Math.Pow(2, (Double)ValueInPacket);
			}

			set
			{
				ValueInPacket = (int)Math.Log(value, 2);
			}
		}
	}
}

