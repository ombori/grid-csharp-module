namespace GridCSharpModule
{
  using System;
  using System.Runtime.Loader;
  using System.Threading;
  using System.Threading.Tasks;
  using System.Text;

  class Program
  {
    static void Main(string[] args)
    {
      var instance = new Program();
      instance.Work().Wait();

      // Wait until the app unloads or is cancelled
      var cts = new CancellationTokenSource();
      AssemblyLoadContext.Default.Unloading += (ctx) => cts.Cancel();
      Console.CancelKeyPress += (sender, cpe) => cts.Cancel();
      WhenCancelled(cts.Token).Wait();
    }

    private class HelloEvent {
      public string Hello {set; get;}
      public int Id {set; get;}
    };

    async private Task Work() {
      var module = new GridOS.Module();
      await module.Connect();

      // Example of receiving a setting value
      var testSetting = module.GetSetting("test_setting");
      Console.WriteLine($"test_setting value {testSetting}");

      // Example of receiving a twin
      var twin = await module.GetTwin();
      Console.WriteLine($"received twin {twin}");

      // Example of a module method
      module.OnMethod<string>("hello", (object args) => {
        Console.WriteLine($"Hello method called {args}");
        return "okay";
      });

      // Example of receiving a message
      module.OnEvent("Test.Event", (object data, string type) => {
        Console.WriteLine($"Event received {type}: {data}");
      });

      // Example of broadcasting a message 
      var broadcastTask = Task.Run(async () => {
        int i = 0;
        var e = new HelloEvent(); 
        e.Hello = "World";

        for(;;)
        {
          await Task.Delay(1000);
          e.Id = i;
          module.Broadcast("Test.Event", e);
          i+=1;
        }
      });

      // Example of subscribing to an MQTT topic
      module.Subscribe("public/test123", (byte[] message, string topic) => {
        var data = Encoding.UTF8.GetString(message);
        Console.WriteLine($"Incoming message on {topic}: {data}");
      });

      // Example of writing to an MQTT topic
      var publishTask = Task.Run(async () => {
        int i = 0;
        for(;;)
        {
          await Task.Delay(1000);
          module.Publish("public/test123", Encoding.UTF8.GetBytes($"Hello {i}"));
          i+=1;
        }
      });
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