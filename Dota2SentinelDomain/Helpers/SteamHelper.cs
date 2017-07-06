using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Dota2SentinelDomain.Helpers {
    public static class SteamHelper {
        public static uint ConvertIDToUint32(ulong input) {
            return Convert.ToUInt32(input - 76561197960265728);
        }

        public static ulong ConvertIDToUint64(ulong input) {
            return input + 76561197960265728;
        }
    }
}
