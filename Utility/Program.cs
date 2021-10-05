namespace Utility
{
    using Autodesk.Forge.Core;
    using Autodesk.Forge.DesignAutomation;
    using Autodesk.Forge.DesignAutomation.Model;
    using Das.WorkItemSigner;
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Logging;
    using Newtonsoft.Json;
    using Newtonsoft.Json.Linq;
    using Serilog;
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Net;
    using System.Net.Http;
    using System.Net.WebSockets;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Defines the <see cref="Specifications" />.
    /// </summary>
    internal static class Specifications
    {
        /// <summary>
        /// Defines the OwnerName.
        /// </summary>
        public const string OwnerName = "fpdmad";

        /// <summary>
        /// Defines the ActivityName.
        /// </summary>
        public const string ActivityName = "DwgCompareInplace";

        /// <summary>
        /// Defines the Alias.
        /// </summary>
        public const string Alias = "prod";

        /// <summary>
        /// Defines the TargetEngine.
        /// </summary>
        public const string TargetEngine = "Autodesk.AutoCAD+24";

        /// <summary>
        /// Defines the FQActivityId.
        /// </summary>
        public const string FQActivityId = "fpdmad.DwgCompareInplace+prod";
    }

    /// <summary>
    /// Defines the <see cref="WSResponse" />.
    /// </summary>
    public class WSResponse
    {
        /// <summary>
        /// Gets or sets the Action.
        /// </summary>
        public string Action { get; set; }

        /// <summary>
        /// Gets or sets the Data.
        /// </summary>
        public WorkItemStatus Data { get; set; }
    }

    /// <summary>
    /// Defines the <see cref="Program" />.
    /// </summary>
    internal class Program
    {
        /// <summary>
        /// Defines the Api.
        /// </summary>
        private static DesignAutomationClient Api;

        /// <summary>
        /// Defines the ws.
        /// </summary>
        private static ClientWebSocket ws = null;

        /// <summary>
        /// The GetOwnerAsync.
        /// </summary>
        /// <param name="clientId">The clientId<see cref="string"/>.</param>
        /// <returns>The <see cref="Task{(string owner, string token)}"/>.</returns>
        private static async Task<(string owner, string token)> GetOwnerAsync(string clientId)
        {
            Console.WriteLine("Setting up owner...");
            var resp = await Api.ForgeAppsApi.GetNicknameAsync("me");
            if (resp.Content == clientId)
            {
                Console.WriteLine("\tNo nickname for this clientId yet. Attempting to create one...");
                HttpResponseMessage response;
                response = await Api.ForgeAppsApi.CreateNicknameAsync("me", 
                                                                    new NicknameRecord() { 
                                                                        Nickname = Specifications.OwnerName },
                                                                    throwOnError: false);
                if (response.StatusCode == HttpStatusCode.Conflict)
                {
                    Console.WriteLine("\tThere are already resources associated with this clientId or nickname is in use. " +
                        "Please use a different clientId or nickname.");
                    return (null, null);
                }
                await response.EnsureSuccessStatusCodeAsync();
                var nickName = await response.Content.ReadAsStringAsync();
                return (nickName, response.RequestMessage.Headers.Authorization.ToString());
            }
            return (resp.Content, resp.HttpResponse.RequestMessage.Headers.Authorization.ToString());
        }

        /// <summary>
        /// The SetupActivityAsync.
        /// </summary>
        /// <returns>The <see cref="Task{string}"/>.</returns>
        private static async Task<string> SetupActivityAsync()
        {
            Console.WriteLine("Setting up activity...");
            var myActivity = $"{Specifications.OwnerName}.{Specifications.ActivityName}+{Specifications.Alias}";
            var actResponse = await Api.ActivitiesApi.GetActivityAsync(myActivity, throwOnError: false);
            var activity = new Activity()
            {

                CommandLine = new List<string>()
                    {
                        $"$(engine.path)\\accoreconsole.exe /i \"$(args[HostDrawing].path)\" /s \"$(settings[script].path)\""
                    },
                Engine = Specifications.TargetEngine,
                Settings = new Dictionary<string, ISetting>()
                    {
                        { "script", new StringSetting() { Value = "COMPAREINPLACE\nON\n-COMPARE\nToCompareWith.dwg\n_SAVEAS\n\nResult.dwg\n" } }
                    },
                Parameters = new Dictionary<string, Parameter>()
                    {
                        { "HostDrawing", new Parameter() { Verb= Verb.Get, LocalName = "$(HostDrawing)",  Required = true } },
                        { "ToCompareWith", new Parameter() { Verb= Verb.Get, LocalName = "ToCompareWith.dwg", Required = true} },
                        { "Result", new Parameter() { Verb= Verb.Post,  LocalName = "Result.dwg", Required= true} }
                    },
                Id = Specifications.ActivityName
            };
            if (actResponse.HttpResponse.StatusCode == HttpStatusCode.NotFound)
            {
                Console.WriteLine($"Creating activity {myActivity}...");
                await Api.CreateActivityAsync(activity, Specifications.Alias);
                return myActivity;
            }
            await actResponse.HttpResponse.EnsureSuccessStatusCodeAsync();
            Console.WriteLine("\tFound existing activity...");
            if (!Equals(activity, actResponse.Content))
            {
                Console.WriteLine($"\tUpdating activity {myActivity}...");
                await Api.UpdateActivityAsync(activity, Specifications.Alias);
            }
            return myActivity;

            bool Equals(Autodesk.Forge.DesignAutomation.Model.Activity a, Autodesk.Forge.DesignAutomation.Model.Activity b)
            {
                Console.Write("\tComparing activities...");
                //ignore id and version
                b.Id = a.Id;
                b.Version = a.Version;
                var res = a.ToString() == b.ToString();
                Console.WriteLine(res ? "Same." : "Different");
                return res;
            }
        }

        /// <summary>
        /// The DownloadToDocsAsync.
        /// </summary>
        /// <param name="url">The url<see cref="string"/>.</param>
        /// <param name="localFile">The localFile<see cref="string"/>.</param>
        /// <returns>The <see cref="Task{string}"/>.</returns>
        private static async Task<string> DownloadToDocsAsync(string url, string localFile)
        {
            var fname = Path.Combine(Environment.CurrentDirectory, localFile);
            using (var client = new HttpClient())
            {
                var content = (await client.GetAsync(url)).Content;
                using var output = System.IO.File.Create(fname);
                (await content.ReadAsStreamAsync()).CopyTo(output);
                output.Close();
            }
            return fname;
        }

        /// <summary>
        /// The Receiving.
        /// </summary>
        /// <returns>The <see cref="Task"/>.</returns>
        private static async Task Receiving()
        {
            var buffer = new byte[4096];
            var shouldExit = false;
            while (!shouldExit)
            {
                var result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);

                if (result.MessageType == WebSocketMessageType.Text)
                {
                    var wsRes = Encoding.UTF8.GetString(buffer, 0, result.Count);
                    WSResponse resp = JsonConvert.DeserializeObject<WSResponse>(wsRes);
                    if (resp.Action.Equals("error"))
                    {
                        Console.WriteLine(wsRes);
                        break;
                    }
                    if (resp.Action.Equals("status"))
                    {

                        Console.WriteLine($"\t{resp.Data.Status}");
                        switch (resp.Data.Status)
                        {
                            case Status.Cancelled:
                            case Status.FailedDownload:
                            case Status.FailedInstructions:
                            case Status.FailedUpload:
                                {
                                    var fname = await DownloadToDocsAsync(resp.Data.ReportUrl,
                                                       $"err_report{DateTime.Now.Ticks}.log");
                                    Console.WriteLine($"\t\tReport Downloaded {fname}");
                                    shouldExit = true;
                                    break;

                                }
                            case Status.Success:
                                {
                                    var fname = await DownloadToDocsAsync(resp.Data.ReportUrl,
                                                       $"ok_report{DateTime.Now.Ticks}.log");
                                    Console.WriteLine($"\t\tReport Downloaded {fname}");
                                    shouldExit = true;
                                    break;
                                }
                        }


                    }

                }
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    await ws.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "ok", CancellationToken.None);
                    break;
                }

            }
        }

        /// <summary>
        /// The Main.
        /// </summary>
        /// <param name="args">The args<see cref="string[]"/>.</param>
        /// <returns>The <see cref="Task"/>.</returns>
        internal static async Task Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            //Step 1: Create Configuration
            /**
             * SET FORGE_CLIENT_ID=<>
             * SET FORGE_CLIENT_SECRET=<>
             */
            var config = new ConfigurationBuilder()
                .AddForgeAlternativeEnvironmentVariables()
                .AddJsonFile("appsettings.user.json", false, true)
                .Build();
            var forgeClientId = config.GetValue<String>("Forge:ClientId");

            var logger = new LoggerConfiguration()
           .WriteTo.Console()
           .CreateLogger();
            //Step 2: Populate Forge Design Automation service,
            //get Design Automation API Client
            Api = new ServiceCollection()
                .AddLogging(builder =>
                {
                    builder.SetMinimumLevel(LogLevel.Information);
                    builder.AddSerilog(logger, dispose: true);

                })
                .AddDesignAutomation(config)
                .Services
                .BuildServiceProvider()
                .GetRequiredService<DesignAutomationClient>();

            //Step 3: Create Owner
            var (owner, token) = await GetOwnerAsync(forgeClientId);
            if (token == null)
            {
                return;
            }

            //Step 4: Create or Update Activity
            await SetupActivityAsync();


            //Step 5: Generating the public signature
            var signer = Signer.Create();
            var publicKeyJson = signer.ToJson(false);


            //Step 6: Uploading public sign your app.
            PublicKey publicKey = JsonConvert.DeserializeObject<PublicKey>(publicKeyJson);
            var nickNameRecord = new NicknameRecord
            {
                PublicKey = publicKey,
                Nickname = Specifications.OwnerName
            };
            await Api.CreateNicknameAsync("me", nickNameRecord);



            //Step 7 Generate digital signature for the activityId

            var signature = new WorkItemSignatures()
            {
                ActivityId = signer.Sign(Specifications.FQActivityId)
            };

            //Step 8: 

            var HostDrawing = new XrefTreeArgument
            {
                Url = "http://download.autodesk.com/us/samplefiles/acad/blocks_and_tables_-_imperial.dwg"
            };
            var ToCompareWith = new XrefTreeArgument
            {
                Url = "http://download.autodesk.com/us/samplefiles/acad/blocks_and_tables_-_imperial.dwg"
            };
            var Result = new XrefTreeArgument
            {
                Url = "https://content.dropboxapi.com/apitul/1/7mjCJzUCtFFHcQ",
                Verb = Verb.Post,
                Headers = new Dictionary<string, string>()
                    {
                    { "Content-Type","application/octet-stream" }
                    }

            };


            var a = new Dictionary<string, IArgument>
                {
                    { "HostDrawing", HostDrawing },
                    { "ToCompareWith", ToCompareWith },
                    { "Result", Result }
                };
            var workItem = new WorkItem
            {
                ActivityId = $"{Specifications.OwnerName}.{Specifications.ActivityName}+{Specifications.Alias}",
                Arguments = a,
                Signatures = signature

            };
            Console.WriteLine(JsonConvert.SerializeObject(workItem, Formatting.Indented));

            var buffer = new byte[4096];
            using (ws = new ClientWebSocket())
            {
                await ws.ConnectAsync(new Uri("wss://websockets.forgedesignautomation.io"), CancellationToken.None);

                JObject wsClientData = new JObject(new JProperty("action", "post-workitem"),
                           new JProperty("data", JObject.FromObject(workItem)),
                           new JProperty("headers",
                           new JObject(new JProperty("Authorization", $"{token}"))));
                var data = JsonConvert.SerializeObject(wsClientData, Formatting.Indented);
                await ws.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(data)), WebSocketMessageType.Text, true, CancellationToken.None);
                //receiving loop
                await Receiving();
                //close
                await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, string.Empty, CancellationToken.None);
                Console.WriteLine("\tDisconnected...");
            }
        }
    }
}
