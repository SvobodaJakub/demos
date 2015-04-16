using System;
using System.Collections;

namespace SntpDemo
{
	

	//                     1                   2                   3
	// 0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9  0  1
	//+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
	//|LI | VN  |Mode |    Stratum    |     Poll      |   Precision    |
	//+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
	//|                          Root  Delay                           |
	//+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
	//|                       Root  Dispersion                         |
	//+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
	//|                     Reference Identifier                       |
	//+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
	//|                                                                |
	//|                    Reference Timestamp (64)                    |
	//|                                                                |
	//+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
	//|                                                                |
	//|                    Originate Timestamp (64)                    |
	//|                                                                |
	//+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
	//|                                                                |
	//|                     Receive Timestamp (64)                     |
	//|                                                                |
	//+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
	//|                                                                |
	//|                     Transmit Timestamp (64)                    |
	//|                                                                |
	//+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
	//|                 Key Identifier (optional) (32)                 |
	//+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
	//|                                                                |
	//|                                                                |
	//|                 Message Digest (optional) (128)                |
	//|                                                                |
	//|                                                                |
	//+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
	

	/// <summary>
	/// Ntp packet. All manipulation can be done through the properties. The specialized properties and WholePacketByteArray convert data between each other.
	/// </summary>
	public class NtpPacket
	{
		public NtpPacket()
		{
			//initialize all properties
			Byte[] blank48 = new Byte[48];
			for (int i = 0; i < 48; i++)
			{
				blank48[i] = 0x00;
			}
			WholePacketByteArray = blank48; //this automatically zeros out all properties except the DestinationTimestamp

			//set the destination timestamp so that an oblivious implementation will still yield correct results
			Timestamp destinationTimestamp = new Timestamp(Helper.DateTimeToNtpTicks(Helper.CurrentUtcDateTime())); //Destination timestamp
			DestinationTimestamp = destinationTimestamp;
		}

		public NtpPacket DeepClone() //creates a deep copy of the ntp packet
		{ //deliberately ignoring ICloneable to make a point this is a _deep_ copy
			NtpPacket clonedPacket = new NtpPacket();
			Byte[] clonedArray = new Byte[48];
			Byte[] oldArray = this.WholePacketByteArray;
			for (int i = 0; i < 48; i++) //deep copy the array for sure (the ntppacket implementation does this at the time of writing this; this is just to make sure)
			{
				clonedArray[i] = oldArray[i];
			}
			clonedPacket.WholePacketByteArray = clonedArray; //this sets all fields to the exact value, except the destination timestamp

			//clone the destination timestamp
			long destinationTicks = this.DestinationTimestamp.Ticks;
			Timestamp clonedDestinationTimestamp = new Timestamp(destinationTicks);
			clonedPacket.DestinationTimestamp = clonedDestinationTimestamp;

			return clonedPacket;
		}

		public int LI { get; set; }
		public int VN { get; set; }
		public int Mode { get; set; }
		public int Stratum { get; set; }
		public PollStruct Poll { get; set; }
		public PrecisionStruct Precision { get; set; }
		public RootDelayStruct RootDelay { get; set; }
		public RootDispersionStruct RootDispersion { get; set; }

		public Byte[] ReferenceIdentifierByteArray {
			//when converting the string to long, the first byte is the most significant
			//in the string DCF0 (0 = 0x00), D is the most significant byte, 0 is the least significant one
			//however, today's computers are little endian, resulting in D being the least significant byte
			//little endian computer converts the string DCF0 to 4604740 whereas a big endian one converts it to 1145259520
			//in order to emulate big endian computer, it is necessary to change the endianness manually
			get {
				//reference identifier 4 bytes
				Byte[] referenceIdentifier8Bytes = BitConverter.GetBytes(ReferenceIdentifierLong);
				Byte[] referenceIdentifier4Bytes = new Byte[4];
				for (int i = 0; i < 4; i++)
				{ //copy the first 4 bytes; this is done because uint32 is not CLS-compliant and int64 is
					referenceIdentifier4Bytes[i] = referenceIdentifier8Bytes[i];
				}
				referenceIdentifier4Bytes = Helper.SwapEndianness4Bytes(referenceIdentifier4Bytes);

				return referenceIdentifier4Bytes;
			}
			set {
				Byte[] referenceIdentifier4Bytes = value;

				referenceIdentifier4Bytes = Helper.SwapEndianness4Bytes(value);
				Byte[] referenceIdentifier8Bytes = new Byte[8];
				for (int i = 0; i < 4; i++) //convert the 4-byte uint32 to 8-byte int64 while preserving CLS-compliancy
				{
					referenceIdentifier8Bytes[i] = referenceIdentifier4Bytes[i];
					referenceIdentifier8Bytes[i + 4] = 0x00;
				}
				ReferenceIdentifierLong = BitConverter.ToInt64(referenceIdentifier8Bytes, 0);
			}
		}


		//not creating a separate struct for ReferenceIdentifier because of using the Stratum field
		public long ReferenceIdentifierLong
		{
			//Reference Identifier: This is a 32-bit bitstring identifying the
			//particular reference source.  This field is significant only in
			//server messages, where for stratum 0 (kiss-o'-death message) and 1
			//(primary server), the value is a four-character ASCII string, left
			//justified and zero padded to 32 bits.  For IPv4 secondary servers,
			//the value is the 32-bit IPv4 address of the synchronization source.
			//For IPv6 and OSI secondary servers, the value is the first 32 bits of
			//the MD5 hash of the IPv6 or NSAP address of the synchronization
			//source.

			//Primary (stratum 1) servers set this field to a code identifying the
			//external reference source according to Figure 2.  If the external
			//reference is one of those listed, the associated code should be used.
			//Codes for sources not listed can be contrived, as appropriate.

			//Code       External Reference Source
			//------------------------------------------------------------------
			//LOCL       uncalibrated local clock
			//CESM       calibrated Cesium clock
			//RBDM       calibrated Rubidium clock
			//PPS        calibrated quartz clock or other pulse-per-second
			//           source
			//IRIG       Inter-Range Instrumentation Group
			//ACTS       NIST telephone modem service
			//USNO       USNO telephone modem service
			//PTB        PTB (Germany) telephone modem service
			//TDF        Allouis (France) Radio 164 kHz
			//DCF        Mainflingen (Germany) Radio 77.5 kHz
			//MSF        Rugby (UK) Radio 60 kHz
			//WWV        Ft. Collins (US) Radio 2.5, 5, 10, 15, 20 MHz
			//WWVB       Boulder (US) Radio 60 kHz
			//WWVH       Kauai Hawaii (US) Radio 2.5, 5, 10, 15 MHz
			//CHU        Ottawa (Canada) Radio 3330, 7335, 14670 kHz
			//LORC       LORAN-C radionavigation system
			//OMEG       OMEGA radionavigation system
			//GPS        Global Positioning Service
			get;
			set;
		} //preserving CLS compliancy

		public Timestamp ReferenceTimestamp { get; set; }
		public Timestamp OriginateTimestamp { get; set; }
		public Timestamp ReceiveTimestamp { get; set; }
		public Timestamp TransmitTimestamp { get; set; }
		public Timestamp DestinationTimestamp { get; set; } //this timestamp is not saved in the NTP packet itself, it is kept for roundtrip delay
		public Byte[] WholePacketByteArray
		{
			get
			{
				//assembly the whole packet

				//LI/VN/Mode byte:
				int liVnMode = 0x00;

				liVnMode = ((LI) << 6); //push the LI value to the MSB
				liVnMode = liVnMode | ((VN) << 3); //push the VN value 3 bytes towards MSB and add it to the byte
				liVnMode = liVnMode | (Mode); //add the Mode

				Byte liVnModeByte = (Byte)liVnMode;

				//stratum byte
				Byte stratumByte = (Byte)Stratum;

				//poll byte
				Byte pollByte = (Byte)Poll.ValueInPacket;

				//precision byte
				Byte precisionByte = (Byte) ((SByte) Precision.ValueInPacket);

				//root delay 4 bytes
				Byte[] rootDelay4Bytes = BitConverter.GetBytes(RootDelay.ValueInPacket);
				rootDelay4Bytes = Helper.SwapEndianness4Bytes(rootDelay4Bytes);

				//root dispersion 4 bytes
				Byte[] rootDispersion8Bytes = BitConverter.GetBytes(RootDispersion.ValueInPacket);
				Byte[] rootDispersion4Bytes = new Byte[4];
				for (int i = 0; i < 4; i++)
				{ //copy the first 4 bytes; this is done because uint32 is not CLS-compliant and int64 is
					rootDispersion4Bytes[i] = rootDispersion8Bytes[i];
				}
				rootDispersion4Bytes = Helper.SwapEndianness4Bytes(rootDispersion4Bytes);

				//reference identifier 4 bytes
				Byte[] referenceIdentifier4Bytes = new Byte[4];
				referenceIdentifier4Bytes = ReferenceIdentifierByteArray;

				//reference timestamp 8 bytes
				Byte[] referenceTimestamp8Bytes = new Byte[8];
				ReferenceTimestamp.TimestampBitArray.CopyTo(referenceTimestamp8Bytes, 0);

				//originate timestamp 8 bytes
				Byte[] originateTimestamp8Bytes = new Byte[8];
				OriginateTimestamp.TimestampBitArray.CopyTo(originateTimestamp8Bytes, 0);

				//receive timestamp 8 bytes
				Byte[] receiveTimestamp8Bytes = new Byte[8];
				ReceiveTimestamp.TimestampBitArray.CopyTo(receiveTimestamp8Bytes, 0);

				//transmit timestamp 8 bytes
				Byte[] transmitTimestamp8Bytes = new Byte[8];
				TransmitTimestamp.TimestampBitArray.CopyTo(transmitTimestamp8Bytes, 0);

				//assemble the packet
				Byte[] wholePacket48Bytes = new Byte[48];
				for (int i = 0; i < 48; i++)
				{
					if (i == 0) wholePacket48Bytes[i] = liVnModeByte;
					if (i == 1) wholePacket48Bytes[i] = stratumByte;
					if (i == 2) wholePacket48Bytes[i] = pollByte;
					if (i == 3) wholePacket48Bytes[i] = precisionByte;
					if ((i >= 4) && (i <= 7)) wholePacket48Bytes[i] = rootDelay4Bytes[i - 4];
					if ((i >= 8) && (i <= 11)) wholePacket48Bytes[i] = rootDispersion4Bytes[i - 8];
					if ((i >= 12) && (i <= 15)) wholePacket48Bytes[i] = referenceIdentifier4Bytes[i - 12];
					if ((i >= 16) && (i <= 23)) wholePacket48Bytes[i] = referenceTimestamp8Bytes[i - 16];
					if ((i >= 24) && (i <= 31)) wholePacket48Bytes[i] = originateTimestamp8Bytes[i - 24];
					if ((i >= 32) && (i <= 39)) wholePacket48Bytes[i] = receiveTimestamp8Bytes[i - 32];
					if ((i >= 40) && (i <= 47)) wholePacket48Bytes[i] = transmitTimestamp8Bytes[i - 40];
				}

				return wholePacket48Bytes;
			}

			set
			{
				Byte[] wholePacket48Bytes = value;
				//disassemble the whole packet
				Byte liVnModeByte = 0x00;
				Byte stratumByte = 0x00;
				Byte pollByte = 0x00;
				Byte precisionByte = 0x00;
				Byte[] rootDelay4Bytes = new Byte[4];
				Byte[] rootDispersion4Bytes = new Byte[4];
				Byte[] referenceIdentifier4Bytes = new Byte[4];
				Byte[] referenceTimestamp8Bytes = new Byte[8];
				Byte[] originateTimestamp8Bytes = new Byte[8];
				Byte[] receiveTimestamp8Bytes = new Byte[8];
				Byte[] transmitTimestamp8Bytes = new Byte[8];

				for (int i = 0; i < 48; i++)
				{
					if (i == 0) liVnModeByte = wholePacket48Bytes[i];
					if (i == 1) stratumByte = wholePacket48Bytes[i];
					if (i == 2) pollByte = wholePacket48Bytes[i];
					if (i == 3) precisionByte = wholePacket48Bytes[i];
					if ((i >= 4) && (i <= 7)) rootDelay4Bytes[i - 4] = wholePacket48Bytes[i];
					if ((i >= 8) && (i <= 11)) rootDispersion4Bytes[i - 8] = wholePacket48Bytes[i];
					if ((i >= 12) && (i <= 15)) referenceIdentifier4Bytes[i - 12] = wholePacket48Bytes[i];
					if ((i >= 16) && (i <= 23)) referenceTimestamp8Bytes[i - 16] = wholePacket48Bytes[i];
					if ((i >= 24) && (i <= 31)) originateTimestamp8Bytes[i - 24] = wholePacket48Bytes[i];
					if ((i >= 32) && (i <= 39)) receiveTimestamp8Bytes[i - 32] = wholePacket48Bytes[i];
					if ((i >= 40) && (i <= 47)) transmitTimestamp8Bytes[i - 40] = wholePacket48Bytes[i];
				}


				//LI/VN/Mode byte:
				int liVnMode = (int)liVnModeByte;
				LI = ((liVnMode & 0xC0 /*11000000*/) >> 6 /*shift towards LSB*/);
				VN = ((liVnMode & 0x38 /*00111000*/) >> 3 /*shift towards LSB*/);
				Mode = ((liVnMode & 0x07 /*00000111*/));

				//stratum byte
				Stratum = stratumByte;

				//poll byte
				PollStruct pollValue = new PollStruct();
				pollValue.ValueInPacket = pollByte;
				Poll = pollValue;

				//precision byte
				PrecisionStruct precisionValue = new PrecisionStruct();
				precisionValue.ValueInPacket = (int) ((SByte) precisionByte);
				Precision = precisionValue;

				//root delay 4 bytes
				rootDelay4Bytes = Helper.SwapEndianness4Bytes(rootDelay4Bytes);
				RootDelayStruct rootDelayValue = new RootDelayStruct();
				rootDelayValue.ValueInPacket = BitConverter.ToInt32(rootDelay4Bytes, 0);
				RootDelay = rootDelayValue;

				//root dispersion 4 bytes
				rootDispersion4Bytes = Helper.SwapEndianness4Bytes(rootDispersion4Bytes);
				Byte[] rootDispersion8Bytes = new Byte[8];
				for (int i = 0; i < 4; i++) //convert the 4-byte uint32 to 8-byte int64 while preserving CLS-compliancy
				{
					rootDispersion8Bytes[i] = rootDispersion4Bytes[i];
					rootDispersion8Bytes[i + 4] = 0x00;
				}
				RootDispersionStruct rootDispersionValue = new RootDispersionStruct();
				rootDispersionValue.ValueInPacket = BitConverter.ToInt64(rootDispersion8Bytes, 0);
				RootDispersion = rootDispersionValue;

				//reference identifier 4 bytes
				ReferenceIdentifierByteArray = referenceIdentifier4Bytes;

				//reference timestamp 8 bytes
				ReferenceTimestamp = new Timestamp(new BitArray(referenceTimestamp8Bytes));

				//originate timestamp 8 bytes
				OriginateTimestamp = new Timestamp(new BitArray(originateTimestamp8Bytes));

				//receive timestamp 8 bytes
				ReceiveTimestamp = new Timestamp(new BitArray(receiveTimestamp8Bytes));

				//transmit timestamp 8 bytes
				TransmitTimestamp = new Timestamp(new BitArray(transmitTimestamp8Bytes));

				//the packet is disassembled and loaded now

			}
		}

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
		public long RoundtripDelay //value in ticks
		{
			get
			{
				long t1 = OriginateTimestamp.Ticks;
				long t2 = ReceiveTimestamp.Ticks;
				long t3 = TransmitTimestamp.Ticks;
				long t4 = DestinationTimestamp.Ticks;

				long d = (t4 - t1) - (t3 - t2);
				return d;
			}
		}

		public long SystemClockOffset //value in ticks
		{
			get
			{
				long t1 = OriginateTimestamp.Ticks;
				long t2 = ReceiveTimestamp.Ticks;
				long t3 = TransmitTimestamp.Ticks;
				long t4 = DestinationTimestamp.Ticks;

				long t = ((t2 - t1) + (t3 - t4)) / 2;
				return t;
			}
		}


	}
}

