namespace GridOS
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

  class Module {
    public MqttClient client = null;
    private int rid = 1000;

    public void Connect()
    {
      string hostname = System.Environment.GetEnvironmentVariable("IOTEDGE_GATEWAYHOSTNAME");
      string ClientId = System.Environment.GetEnvironmentVariable("IOTEDGE_DEVICEID");
      string ModuleId = System.Environment.GetEnvironmentVariable("IOTEDGE_MODULEID");
      string HubName = System.Environment.GetEnvironmentVariable("IOTEDGE_IOTHUBHOSTNAME");
      string SasToken = "SharedAccessSignature sr=grid-admin-hub.azure-devices.net%2Fdevices%2Fb6ab4904-7a33-49ad-be4e-9cdecaad1530%2Fmodules%2Fgrid-csharp-module&sig=ARVGJaGxxLxMDpZmdSt%2Fz0Nd%2BUVwGFdgWD0E7%2BAqumk%3D&se=1617765137";

      client = new MqttClient(hostname);
      client.ProtocolVersion = MqttProtocolVersion.Version_3_1_1;
      
      var result = client.Connect(
                        $"{ClientId}/{ModuleId}",
                        $"{HubName}/{ClientId}/{ModuleId}/?api-version=2018-06-30",
                        SasToken);

      if (result != MqttMsgConnack.CONN_ACCEPTED)
      {
          throw new Exception("Mqtt server rejected connection");
      }

      Console.WriteLine("IoT client connected");

      client.MqttMsgPublishReceived += Client_OnMessage;

      // To receive the result of a Twin-Get or a Twin-Patch, a client needs to subscribe
      // to the following topic:
      client.Subscribe(
          new[] { "$iothub/twin/res/#" },
          new[] { (byte)1 });

      // To receive Direct Method Calls, a client needs to subscribe to the following topic:
      client.Subscribe(
          new[] { "$iothub/methods/POST/#" },
          new[] { (byte)1 });      
    }

    public Task GetTwin() {
      if (client == null) throw new Exception("Client not initialized");
      
      // Request a twin
      client.Publish(
        $"$iothub/twin/GET/?$rid={rid}",
        new[] { (byte)1 });

      rid += 1;

      // TODO: register callback 
      // TODO: wait for callback
      // TODO: timeout, remove callback

      return null;
    }

    private static string CustomMethod(string name, string value) {
      Console.WriteLine($"Method ${name}: ${value}");
      return "okay";
    }

    private static void Client_OnMessage(object sender, MqttMsgPublishEventArgs e)
    {
      var topic = e.Topic;
      var message = Encoding.UTF8.GetString(e.Message);

      if (topic.StartsWith($"$iothub/methods/POST/"))
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