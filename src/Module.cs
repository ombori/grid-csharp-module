namespace GridOS
{
  // TODO: add emit
  // TODO: add settings

  using System;
  using System.Text;
  using uPLibrary.Networking.M2Mqtt;
  using uPLibrary.Networking.M2Mqtt.Messages;

  using System.Threading.Tasks;
  using System.Text.Json;

  class Module {
    public MqttClient client = null;
    private int rid = 1000;

    private string DeviceId = null;
    private string ModuleId = null;

    public void Connect()
    {
      string hostname = System.Environment.GetEnvironmentVariable("IOTEDGE_GATEWAYHOSTNAME");
      DeviceId = System.Environment.GetEnvironmentVariable("IOTEDGE_DEVICEID");
      ModuleId = System.Environment.GetEnvironmentVariable("IOTEDGE_MODULEID");
      string HubName = System.Environment.GetEnvironmentVariable("IOTEDGE_IOTHUBHOSTNAME");
      string SasToken = "SharedAccessSignature sr=grid-admin-hub.azure-devices.net%2Fdevices%2Fb6ab4904-7a33-49ad-be4e-9cdecaad1530%2Fmodules%2Fgrid-csharp-module&sig=gSFKqAalWF3qRkY8G9LL7QZAPj2enNlV5R4JOZ6vADI%3D&se=1617899054";

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

      // To receive the result of a Twin-Get or a Twin-Patch, a client needs to subscribe
      // to the following topic:
      client.Subscribe(
        new[] { "$iothub/twin/res/#" },
        new[] { (byte)1 });

      // To receive Direct Method Calls, a client needs to subscribe to the following topic:
      client.Subscribe(
        new[] { "$iothub/methods/POST/#" },
        new[] { (byte)1 });   

      // Grid local bradcasts
      client.Subscribe(
        new[] {"ombori/grid/message/#" },
        new[] { (byte)1 });
    }

    public string GetSetting(string name) {
      return System.Environment.GetEnvironmentVariable($"{ModuleId}_{name}".ToUpper());
    }

    public Task<object> GetTwin() {
      if (client == null) throw new Exception("Client not initialized");

      var id = rid.ToString();
      rid+=1;

      var result = new TaskCompletionSource<object>();
      var filter = $"$iothub/twin/res/";

      client.MqttMsgPublishReceived += (object sender, MqttMsgPublishEventArgs e) => {
        var topic = e.Topic;
        if (!topic.StartsWith(filter)) return;

        var messageId = topic.Split("=")[1];
        if (messageId != id) return;

        var code = topic.Split("/")[3];
        if (code != "200") throw new Exception($"Cannot fetch twin, code={code}");

        result.SetResult(JsonSerializer.Deserialize<object>(e.Message));

        // TODO: remove handler
        // client.MqttMsgPublishReceived -= eventHandler;
        return;
      };

      client.Publish(
        $"$iothub/twin/GET/?$rid={id}",
        new[] { (byte)1 });

      return result.Task;
    }
    
    public void OnMethod<R>(string name, Func<object, Task<R>> callback) {
      var filter = $"$iothub/methods/POST/{name}/?";
      client.MqttMsgPublishReceived += async (object sender, MqttMsgPublishEventArgs e) => {
        var topic = e.Topic;
        if (!topic.StartsWith(filter)) return;

        var rid = topic.Split("=")[1];

        try {
          var args = JsonSerializer.Deserialize<object>(e.Message);
          R result = await callback(args);
          var json = JsonSerializer.Serialize(result);
          client.Publish($"$iothub/methods/res/200/?$rid={rid}", Encoding.UTF8.GetBytes(json));
        } catch (Exception error) {
          client.Publish($"$iothub/methods/res/500/?$rid={rid}", Encoding.UTF8.GetBytes(error.Message));
        }
      };
    }

    public void OnMethod<R>(string name, Func<object, R> handler) {
      var filter = $"$iothub/methods/POST/{name}/?";
      client.MqttMsgPublishReceived += (object sender, MqttMsgPublishEventArgs e) => {
        var topic = e.Topic;
        if (!topic.StartsWith(filter)) return;

        var rid = topic.Split("=")[1];

        try {
          var args = JsonSerializer.Deserialize<object>(e.Message);
          R result = handler(args);
          var json = JsonSerializer.Serialize(result);
          client.Publish($"$iothub/methods/res/200/?$rid={rid}", Encoding.UTF8.GetBytes(json));
        } catch (Exception error) {
          client.Publish($"$iothub/methods/res/500/?$rid={rid}", Encoding.UTF8.GetBytes(error.Message));
        }
      };
    }

    public void OnEvent(string type, Action<object, string> handler) {
      var filter = $"ombori/grid/message/{type}";
      client.MqttMsgPublishReceived += (object sender, MqttMsgPublishEventArgs e) => {
        var topic = e.Topic;
        if (topic != filter) return;

        var args = JsonSerializer.Deserialize<object>(e.Message);
        handler(args, type);
      };
    }

    public void Broadcast(string type, object payload) {
      var json = JsonSerializer.Serialize(payload);
      client.Publish($"ombori/grid/message/{type}", Encoding.UTF8.GetBytes(json));
    }

    public void Publish(string topic, byte[] payload) {
      client.Publish(topic, payload);
    }

    public void Subscribe(string topic, Action<byte[], string> handler) {
      client.Subscribe(
        new[] { topic },
        new[] { (byte)1 });

      client.MqttMsgPublishReceived += (object sender, MqttMsgPublishEventArgs e) => {
        if (e.Topic != topic) return;
        handler(e.Message, topic);
      };
    }
  }
}