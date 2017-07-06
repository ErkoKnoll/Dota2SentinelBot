using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Dota2SentinelDomain;
using Microsoft.EntityFrameworkCore;
using Dota2SentinelCoordinatorBot;
using System.Threading;
using log4net.Config;
using System.IO;
using Dota2SentinelDomain.Models.Config;
using Newtonsoft.Json;
using System.Diagnostics;
using log4net;
using Dota2SentinelHost.Controllers;
using Dota2SentinelHost.Jobs;
using System.Reflection;

namespace Dota2SentinelHost {
    public class Startup {
        public static Config Config { get; set; }
        public static CoordinatorBot Bot { get; set; }
        private static MatchCheckerJob MatchCheckerJob { get; set; }
        private ILog _logger = LogManager.GetLogger(typeof(Startup));

        public Startup(IHostingEnvironment env) {
            var builder = new ConfigurationBuilder()
                .SetBasePath(env.ContentRootPath)
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                .AddJsonFile($"appsettings.{env.EnvironmentName}.json", optional: true)
                .AddEnvironmentVariables();
            Configuration = builder.Build();

            var basePath = env.WebRootPath.Replace("wwwroot", "");
            var logRepository = LogManager.GetRepository(Assembly.GetEntryAssembly());
            XmlConfigurator.Configure(logRepository, new FileInfo(Path.Combine(basePath, "Config\\log4net.xml")));

            Startup.Config = JsonConvert.DeserializeObject<Config>(File.ReadAllText(Path.Combine(basePath, "Config\\config.json")));
        }

        public IConfigurationRoot Configuration { get; }

        public void ConfigureServices(IServiceCollection services) {
            services.AddDbContext<Repository>(options => options.UseNpgsql(
                Startup.Config.ConnectionString,
                b => b.MigrationsAssembly("Dota2SentinelHost"))
            );

            services.AddScoped<IRepository, Repository>();

            services.AddMvc();
        }

        public void Configure(IApplicationBuilder app, IHostingEnvironment env, ILoggerFactory loggerFactory) {
            loggerFactory.AddConsole(Configuration.GetSection("Logging"));
            loggerFactory.AddDebug();

            if (env.IsDevelopment()) {
                app.UseDeveloperExceptionPage();
                app.UseBrowserLink();
            } else {
                app.UseExceptionHandler("/Home/Error");
            }

            app.UseStaticFiles();

            app.UseMvc(routes => {
                routes.MapRoute(
                    name: "default",
                    template: "{controller=Home}/{action=Index}/{id?}");
            });

            using (var serviceScope = app.ApplicationServices.GetRequiredService<IServiceScopeFactory>().CreateScope()) {
                serviceScope.ServiceProvider.GetService<Repository>().Database.Migrate();
            }

            var lobbyBotProcesses = new List<Process>();

            new Thread(() => {
                Thread.CurrentThread.IsBackground = true;
                Startup.MatchCheckerJob = new MatchCheckerJob();
                Startup.Bot = new CoordinatorBot(Startup.Config);
                Startup.Bot.OutboundMessages = CoordinatorController.OutboundMessages;
                Startup.Bot.LobbyStates = CoordinatorController.LobbyStates;
                Startup.Bot.OnGetPlayerMatchHistoryResponse = Startup.MatchCheckerJob.OnGetPlayerMatchHistoryResponse;
                Startup.Bot.OnMatchDetailsResponse = Startup.MatchCheckerJob.OnMatchDetailsResponse;
                Startup.Bot.OnStartup = () => Startup.MatchCheckerJob.StartJob();
                Startup.Bot.OnShutdown = () => {
                    foreach (var lobbyBotProcess in lobbyBotProcesses) {
                        try {
                            lobbyBotProcess.Kill();
                        } catch (Exception e) {
                            _logger.Error("Lobby bot process failed to kill", e);
                        }
                    }
                    Environment.Exit(0);
                };
                Startup.Bot.Connect();
            }).Start();

            var log4NetConfigurationPath = Path.Combine(env.WebRootPath.Replace("wwwroot", ""), "Config\\log4net.xml");
            var test = false;
#if DEBUG
            test = true;
#endif

            foreach (var lobbyBot in Startup.Config.LobbyBotsPool) {
                lobbyBotProcesses.Add(Process.Start(Startup.Config.LobbyBotPath, String.Format("{0} {1} \"{2}\" \"{3}\" \"{4}\" {5} {6}", lobbyBot.UserName, lobbyBot.Password, log4NetConfigurationPath, Startup.Config.CoordinatorUrl, Startup.Config.ConnectionString, test, lobbyBot.MagicNumber)));
            }
        }
    }
}
