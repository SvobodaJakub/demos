using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace SntpDemo
{

	/// <summary>
	/// Constants. Remnants from what this class used to be in the original usecase.
	/// </summary>
    static class Constants
    {

        public static int NetworkTimeoutMs = 10000; // 10 s
        public static int SntpNetworkTimeoutMs = 30000; // 30 s
        public static int SntpTimeBetweenRetriesMs = 15000; //15 s
		
    }
}
