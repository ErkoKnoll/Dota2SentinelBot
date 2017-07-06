using System;

namespace Dota2SentinelMagicNumberGenerator {
    class Program {
        static void Main(string[] args) {
            if (args.Length != 3) {
                Console.WriteLine("Required arguments are: [steamUserId] [lobbyId] [readyUpKey]");
            } else {
                ulong userId = ulong.Parse(args[0]);
                ulong lobbyId = ulong.Parse(args[1]);
                ulong actualResult = ulong.Parse(args[2]);
                ulong computedResult = lobbyId ^ ~(userId | (userId << 32));
                Console.WriteLine("Magic Number: " + (actualResult - computedResult));
            }
        }
    }
}