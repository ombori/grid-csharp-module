namespace GridCSharpModule
{
  using System;
  using System.Runtime.Loader;
  using System.Threading;
  using System.Threading.Tasks;

  using Ombori.Grid;

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

    private class Settings {
      public string testSetting {set; get;}
    }

    private class HelloEvent {
      public string Hello {set; get;}
      public int Id {set; get;}
    };

    async private Task Work() {
      var module = new Module<Settings>();
      await module.Connect();

      // Example of receiving initial setting value
      Console.WriteLine($"testSetting value: {module.settings.testSetting}");

      // Example of receiving setting update
      module.OnSettings((Settings data) => {
        Console.WriteLine($"testSetting updated: {data.testSetting}");
      });

      // Example of a module method
      module.OnMethod<string>("hello", (object args) => {
        Console.WriteLine($"Hello method called {args}");
        return "okay";
      });

      // Example of receiving a message
      module.Subscribe<HelloEvent>("Test.Event", (data, type) => {
        Console.WriteLine($"Event received {type}: hello={data.Hello} id={data.Id}");
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
          try {
            await module.Publish("Test.Event", e);
          } catch {
            Console.WriteLine("Cannot broadcast");
          }
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