using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Dota2SentinelDomain.Models.Config;
using System.IO;

namespace Dota2SentinelDomain {
    //Only used for database migrations
    public class RepositoryFactory : IDbContextFactory<Repository> {
        public Repository Create(DbContextFactoryOptions options) {
            //Change this to your path
            var projectPath = @"D:\VS Workspace\Dota2SentinelBot\Dota2SentinelHost";
            var config = JsonConvert.DeserializeObject<Config>(File.ReadAllText(Path.Combine(projectPath, @"Config\config.json")));
            return new Repository(new DbContextOptionsBuilder<Repository>().UseNpgsql(config.ConnectionString, b => b.MigrationsAssembly("Dota2SentinelHost")).Options);
        }
    }
}
