using System;

namespace SntpDemo
{
	public struct RootDelayStruct
	{
		public int ValueInPacket { get; set; } //32-bit signed number
		public Double RootDelayValueSeconds
		{
			//Root Delay: This is a 32-bit signed fixed-point number indicating the
			//total roundtrip delay to the primary reference source, in seconds
			//with the fraction point between bits 15 and 16.  Note that this
			//variable can take on both positive and negative values, depending on
			//the relative time and frequency offsets.  This field is significant
			//only in server messages, where the values range from negative values
			//of a few milliseconds to positive values of several hundred
			//milliseconds.

			//                     1                   2                   3
			// 0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9  0  1
			//+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
			//|LI | VN  |Mode |    Stratum    |     Poll      |   Precision    |
			//+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
			//|                          Root .Delay                           |
			//+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+

			//fraction point after bit 31: interpretation = value / 2^0
			//fraction point after bit 30: interpretation = value / 2^1
			//fraction point after bit 29: interpretation = value / 2^2
			//fraction point after bit 19: interpretation = value / 2^12
			//fraction point after bit 17: interpretation = value / 2^14
			//fraction point after bit 16: interpretation = value / 2^15
			//fraction point after bit 15: interpretation = value / 2^16
			//2^16 = 0x10000

			get
			{
				return (((Double)ValueInPacket) / ((Double)0x10000));
			}
			set
			{
				ValueInPacket = (int)(value * 0x10000);
			}
		}
	}
}

