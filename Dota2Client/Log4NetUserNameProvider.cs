using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Dota2Client
{
    public class Log4NetUserNameProvider
    {
        public override string ToString() {
            return DotaClient.UserName;
        }
    }
}
