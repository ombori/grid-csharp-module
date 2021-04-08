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
  using System.Globalization;
  using System.Net;
  using System.Linq;
  using System.Text.Encodings.Web;
  using System.Threading;
  using System.Net.Sockets;

  class Module {
    public MqttClient client = null;
    private int rid = 1000;

    private string DeviceId = null;
    private string ModuleId = null;
    private string IotHubHostName = null;

    private class SignRequest {
      public string algo { get; set; }
      public string keyId { get; set; }
      public string data { get; set; }
    }

    private class SignResponse
    {
      public byte[] digest { get; set; }
    }

    private string Sign(byte[] data, CancellationToken token = default)
    {
      var genId = UrlEncoder.Default.Encode(Environment.GetEnvironmentVariable("IOTEDGE_MODULEGENERATIONID"));
      var apiVer = UrlEncoder.Default.Encode(Environment.GetEnvironmentVariable("IOTEDGE_APIVERSION"));
      var workload = Environment.GetEnvironmentVariable("IOTEDGE_WORKLOADURI");

      string module = UrlEncoder.Default.Encode(ModuleId);
      var host = workload.Split('/').Last();

      var reqData = new SignRequest() {algo = "HMACSHA256", keyId = "primary", data = System.Convert.ToBase64String(data)};
      var payload = JsonSerializer.Serialize(reqData);
      var req = 
        $"POST http://{host}/modules/{module}/genid/{genId}/sign?api-version={apiVer} HTTP/1.1\r\n" +
        $"Content-Type: application/json\r\n" +
        $"Content-Length: {payload.Length}\r\n" + 
        $"\r\n" + 
        payload;

      var sock = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.IP);
      sock.Connect(new UnixDomainSocketEndPoint(workload.Split("://").Last()));

      sock.Send(Encoding.ASCII.GetBytes(req));

      byte[] buf = new byte[4096];
      sock.Receive(buf);
      sock.Close();

      var lines = Encoding.ASCII.GetString(buf).Trim().Split("\n");
      var first = lines.First().Trim();
      var last = lines.Last().Trim('\0');

      if(first != "HTTP/1.1 200 OK") throw new Exception("Cannot sign");

      var res = JsonSerializer.Deserialize<SignResponse>(Encoding.ASCII.GetBytes(last));
      return Convert.ToBase64String(res.digest);
    }

    public string GetModuleToken(int expiryInSeconds = 3600)
    { 
      var expiry = DateTimeOffset.Now.ToUnixTimeSeconds() + expiryInSeconds;
      var resource = WebUtility.UrlEncode($"{IotHubHostName}/devices/{DeviceId}/modules/{ModuleId}");
      var dataToSign = Encoding.UTF8.GetBytes($"{resource}\n{expiry}");

      var signature = WebUtility.UrlEncode(this.Sign(dataToSign)); 
      return $"SharedAccessSignature sr={resource}&sig={signature}&se={expiry}";
    }

    public Boolean Connect()
    {
      var hostname = System.Environment.GetEnvironmentVariable("IOTEDGE_GATEWAYHOSTNAME");
      DeviceId = System.Environment.GetEnvironmentVariable("IOTEDGE_DEVICEID");
      ModuleId = System.Environment.GetEnvironmentVariable("IOTEDGE_MODULEID");
      IotHubHostName = System.Environment.GetEnvironmentVariable("IOTEDGE_IOTHUBHOSTNAME");

      string password = GetModuleToken(3600);

      client = new MqttClient(hostname);
      client.ProtocolVersion = MqttProtocolVersion.Version_3_1_1;
    
      var result = client.Connect(
                        $"{DeviceId}/{ModuleId}",
                        $"{IotHubHostName}/{DeviceId}/{ModuleId}/?api-version=2018-06-30",
                        password);

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

      return true;
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