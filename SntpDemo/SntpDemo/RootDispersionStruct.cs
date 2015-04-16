using System;

namespace SntpDemo
{
	public struct RootDispersionStruct
	{
		public long ValueInPacket { get; set; } //preserving CLS compliancy
		public Double RootDispersionValueSeconds
		{
			//Root Dispersion: This is a 32-bit unsigned fixed-point number
			//indicating the maximum error due to the clock frequency tolerance, in
			//seconds with the fraction point between bits 15 and 16.  This field
			//is significant only in server messages, where the values range from
			//zero to several hundred milliseconds.


			//fraction point after bit 31: interpretation = value / 2^0
			//fraction point after bit 15: interpretation = value / 2^16
			//2^16 = 0x10000

			get
			{
				return (((Double)ValueInPacket) / ((Double)0x10000));
			}
			set
			{
				ValueInPacket = (long)(value * 0x10000);
			}
		}
	}
}

