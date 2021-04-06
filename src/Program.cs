namespace BrokerQuickStart
{
  using System;
  using System.IO;
  using System.Net.Security;    
  using System.Security.Cryptography.X509Certificates;
  using System.Text;
  using uPLibrary.Networking.M2Mqtt;
  using uPLibrary.Networking.M2Mqtt.Messages;

  using System.Runtime.Loader;
  using System.Threading;
  using System.Threading.Tasks;
  using System.Text.Json;

  class Program
  {
    static void Main(string[] args)
    {
      var module = new GridOS.Module();
      module.Connect();

      // Wait until the app unloads or is cancelled
      var cts = new CancellationTokenSource();
      AssemblyLoadContext.Default.Unloading += (ctx) => cts.Cancel();
      Console.CancelKeyPress += (sender, cpe) => cts.Cancel();
      WhenCancelled(cts.Token).Wait();
    }

    // Handles cleanup operations when app is cancelled or unloads
    public static Task WhenCancelled(CancellationToken cancellationToken)
    {
      var tcs = new TaskCompletionSource<bool>();
      cancellationToken.Register(s => ((TaskCompletionSource<bool>)s).SetResult(true), tcs);
      return tcs.Task;
    }
  }
}

// namespace GridModuleExample
// {
//   using System;
//   using System.Runtime.Loader;
//   using System.Text;
//   using System.Threading;
//   using System.Threading.Tasks;
//   using Microsoft.Azure.Devices.Client;
//   using Microsoft.Azure.Devices.Client.Transport.Mqtt;
//   using System.Text.Json;

//   class Program
//   {
//     static void Main(string[] args)
//     {
//       var instance = new Program();
//       instance.Init().Wait();

//       // Wait until the app unloads or is cancelled
//       var cts = new CancellationTokenSource();
//       AssemblyLoadContext.Default.Unloading += (ctx) => cts.Cancel();
//       Console.CancelKeyPress += (sender, cpe) => cts.Cancel();
//       WhenCancelled(cts.Token).Wait();
//     }

//     // Handles cleanup operations when app is cancelled or unloads
//     public static Task WhenCancelled(CancellationToken cancellationToken)
//     {
//         var tcs = new TaskCompletionSource<bool>();
//         cancellationToken.Register(s => ((TaskCompletionSource<bool>)s).SetResult(true), tcs);
//         return tcs.Task;
//     }

//     // Initializes the ModuleClient and sets up the callback to receive
//     // messages containing temperature information
//     async Task Init()
//     {
//       MqttTransportSettings mqttSetting = new MqttTransportSettings(TransportType.Mqtt_Tcp_Only);
//       ITransportSettings[] settings = { mqttSetting };

//       // TODO: Update 'name' field in package.json with 'organisation-name.module-name' identifier
//       // TODO: Update 'description' field in package.json with module's descriptive name
//       // TODO: Update 'container-registry' field in package.json with your container registry hostname
//       // TODO: Create .env file with <your-registry>_USERNAME and <your-registry>_PASSWORD values

//       // Open a connection to the Edge runtime
//       ModuleClient ioTHubModuleClient = await ModuleClient.CreateFromEnvironmentAsync(settings);
//       await ioTHubModuleClient.OpenAsync();
//       Console.WriteLine("IoT Hub module client initialized.");

//       // Receive setting value
//       var moduleName = "grid-csharp-module";
//       var value = System.Environment.GetEnvironmentVariable((moduleName + "_" + "test_setting").ToUpper());
//       Console.WriteLine("test_setting value is", value);

//       // In this example we send TestModule.Event message every second
//       // int seq = 0;
//       // setInterval(() => {
//       //   module.broadcast('MyModule.Event', { some: 'data', seq });
//       //   seq += 1;
//       // }, 1000);

//       // Example of module method
//       await ioTHubModuleClient.SetMethodHandlerAsync("someMethod", SomeMethod, null);

//       // Example of MQTT topic subscription
//       // module.subscribe('ombori', (data, topic) => {
//       //   console.log(`${topic}> `, data.toString());
//       // });

//       // Example of publishing to MQTT topic
//       // int mqttSeq = 0;
//       // setInterval(() => {
//       //   module.publish('ombori', `hello ${mqttSeq}`);
//       //   mqttSeq += 1;
//       // }, 1000);
//     }

//     // Method handler
//     private Task<MethodResponse> SomeMethod(MethodRequest methodRequest, object userContext)
//     {
//       Console.WriteLine($"\t *** {methodRequest.Name} was called.");
//       Console.WriteLine($"\t{methodRequest.DataAsJson}\n");

//       string result = JsonSerializer.Serialize("hello there");
//       return Task.FromResult(new MethodResponse(Encoding.UTF8.GetBytes(result), 200));
//     }
//   }
// }
