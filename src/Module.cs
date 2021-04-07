namespace GridOS
{
  using System;
  using System.Collections.Generic;
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

  //"$iothub/<deviceid>/<moduleid>/messages/events" -  "iothub telemetry"
  //"$iothub/<deviceid>/<moduleid>/messages/c2d/post" -  "iothub c2d messages"
  //"$iothub/<deviceid>/<moduleid>/twin/desired" -  "iothub update desired properties"
  //"$iothub/<deviceid>/<moduleid>/twin/reported" -  "iothub update reported properties"
  //"$iothub/<deviceid>/<moduleid>/twin/get" -  "iothub device twin request"
  //"$iothub/<deviceid>/<moduleid>/twin/res/<status>" -  "iothub device twin response"
  //"$iothub/<deviceid>/<moduleid>/methods/post" -  "iothub device DM request"
  //"$iothub/<deviceid>/<moduleid>/methods/res/<status>" -  "iothub device DM response"
  
  class Module {
    public MqttClient client = null;
    private int rid = 1000;

    private string DeviceId = null;
    private string ModuleId = null;

    private Dictionary<string, MqttClient.MqttMsgPublishEventHandler> handlers;

    public void Connect()
    {
      string hostname = System.Environment.GetEnvironmentVariable("IOTEDGE_GATEWAYHOSTNAME");
      DeviceId = System.Environment.GetEnvironmentVariable("IOTEDGE_DEVICEID");
      ModuleId = System.Environment.GetEnvironmentVariable("IOTEDGE_MODULEID");
      string HubName = System.Environment.GetEnvironmentVariable("IOTEDGE_IOTHUBHOSTNAME");
      string SasToken = "SharedAccessSignature sr=grid-admin-hub.azure-devices.net%2Fdevices%2Fb6ab4904-7a33-49ad-be4e-9cdecaad1530%2Fmodules%2Fgrid-csharp-module&sig=h%2Bai4%2B334V3Sn2bONy56NJkaMSWnTMr9fXJdgoWK9q0%3D&se=1617837820";

      client = new MqttClient(hostname);
      client.ProtocolVersion = MqttProtocolVersion.Version_3_1_1;
      
      var result = client.Connect(
                        $"{DeviceId}/{ModuleId}",
                        $"{HubName}/{DeviceId}/{ModuleId}/?api-version=2018-06-30",
                        SasToken);

      if (result != MqttMsgConnack.CONN_ACCEPTED)
      {
          throw new Exception("Mqtt server rejected connection");
      }

      Console.WriteLine("IoT client connected");

      client.MqttMsgPublishReceived += Client_OnMessage;


      client.Subscribe(
          new[] { "#" },
          new[] { (byte)1 });

      // To receive the result of a Twin-Get or a Twin-Patch, a client needs to subscribe
      // to the following topic:
      client.Subscribe(
          new[] { "$iothub/twin/res/#" },
          new[] { (byte)1 });

      // To receive Direct Method Calls, a client needs to subscribe to the following topic:
      client.Subscribe(
          new[] { $"$iothub/{DeviceId}/{ModuleId}methods/POST/#" },
          new[] { (byte)1 });      

      // twin results and updates
      client.Subscribe(
        new[] { $"$iothub/{DeviceId}/{ModuleId}/twin/res/#" }, 
        new[] {(byte)1}
      );
    }

    public Task<string> GetTwin() {
      Console.WriteLine("GetTwin");
      if (client == null) throw new Exception("Client not initialized");

      var id = rid.ToString();
      rid+=1;
      

      Console.WriteLine($"rid={id}");
      var result = new TaskCompletionSource<string>();
      var filter = $"$iothub/{DeviceId}/{ModuleId}/twin/res/";

      // here is your global onMessage handler i guess

      client.MqttMsgPublishReceived += (object sender, MqttMsgPublishEventArgs e) => {

        Console.WriteLine($"<{e.Topic}");

        var topic = e.Topic;
        if (!topic.StartsWith(filter)) return;

        var messageId = topic.Split("=")[2];
        if (messageId != id) return;
      
        var message = Encoding.UTF8.GetString(e.Message);

        Console.WriteLine($"done");
        result.SetResult(message);

        // TODO: remove handler
        // client.MqttMsgPublishReceived -= eventHandler;
        return;
      };

      // Request a twin

      Console.WriteLine($">$iothub/{DeviceId}/{ModuleId}/twin/GET/?$rid={id}");
      client.Publish(
        $"$iothub/{DeviceId}/{ModuleId}/twin/GET/?$rid={id}",
        new[] { (byte)1 });

      return result.Task;
    }

    private string CustomMethod(string name, string value) {
      Console.WriteLine($"Method ${name}: ${value}");
      return "okay";
    }

    private void Client_OnMessage(object sender, MqttMsgPublishEventArgs e)
    {
      var topic = e.Topic;
      var message = Encoding.UTF8.GetString(e.Message);

      if (topic.StartsWith($"$iothub/{DeviceId}/{ModuleId}/methods/POST/"))
      {
        var rid = topic.Split('=')[1];
        Console.WriteLine($"rid={rid}");

        var result = CustomMethod(topic.Split("/")[2], message);

        var respTopic = $"$iothub/methods/res/200/{rid}";
        Console.WriteLine($"res to {respTopic}\n\t{result}");

        var client = sender as MqttClient;
        client.Publish(respTopic, Encoding.UTF8.GetBytes(result));
      }

      Console.WriteLine($"Received:\n\t{topic}\n\t{message}");
    }
  }
}