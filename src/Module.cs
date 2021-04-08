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
  using Microsoft.Azure.EventGridEdge.IotEdge;
  using System.Globalization;
  using System.Net;
  using System.Linq;
  using System.Net.Http;
  using System.Net.Http.Headers;
  using System.Text.Encodings.Web;
  using System.Threading;

  class Module {
    public MqttClient client = null;
    private int rid = 1000;

    private string DeviceId = null;
    private string ModuleId = null;
    private string IotHubHostName = null;

    private class SignRequest
    {
      public string keyId { get; set; }
      public string algo { get; set; }
      public byte[] data { get; set; }
    }

    private class SignResponse
    {
      public byte[] digest { get; set; }
    }

    HttpClient httpClient;

    private async Task<SignResponse> SignAsync(byte[] data, CancellationToken token = default)
    {
      var IotHubName = this.IotHubHostName.Split('.').FirstOrDefault();
      var workloadUri = new Uri(workloadUriString);

      string baseUrlForRequests = $"http://{workloadUri.Segments.Last()}";
      this.httpClient = new HttpClient(new HttpUdsMessageHandler(workloadUri));

      this.httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

      string encodedApiVersion = UrlEncoder.Default.Encode(this.workloadApiVersion);
      string encodedModuleId = UrlEncoder.Default.Encode(this.ModuleId);
      string encodedModuleGenerationId = UrlEncoder.Default.Encode(this.moduleGenerationId);

      var uri = new Uri($"{baseUrlForRequests}/modules/{encodedModuleId}/genid/{encodedModuleGenerationId}/sign?api-version={encodedApiVersion}");

      var request = new SignRequest
      {
          algo = "HMACSHA256",
          data = data,
          keyId = "primary"
      };

      string requestString = JsonSerializer.Serialize(request);
      using (var content = new StringContent(requestString, Encoding.UTF8, "application/json"))
      using (var httpRequest = new HttpRequestMessage(HttpMethod.Post, uri) { Content = content })
      using (HttpResponseMessage httpResponse = await this.httpClient.SendAsync(httpRequest, token))
      {
        string responsePayload = await httpResponse.Content.ReadAsStringAsync();
        if (httpResponse.StatusCode == HttpStatusCode.OK)
        {
          SignResponse signResponse = JsonSerializer.Deserialize<SignResponse>(responsePayload);
          return signResponse;
        }

        throw new InvalidOperationException($"Failed to execute sign request from IoTEdge security daemon. StatusCode={httpResponse.StatusCode} ReasonPhrase='{httpResponse.ReasonPhrase}' ResponsePayload='{responsePayload}' Request={requestString} This={this}");
      }
    }

    public async Task<string> GetModuleToken(int expiryInSeconds = 3600)
    { 
      TimeSpan fromEpochStart = DateTime.UtcNow - new DateTime(1970, 1, 1);
      string expiry = Convert.ToString((int)fromEpochStart.TotalSeconds + expiryInSeconds);
      string resourceUri = $"{IotHubHostName}/devices/{DeviceId}/modules/{ModuleId}";
      string stringToSign = WebUtility.UrlEncode(resourceUri) + "\n" + expiry;
      var signResponse = await this.SignAsync(Encoding.UTF8.GetBytes(stringToSign));
      var signature = Convert.ToBase64String(signResponse.digest);
      string token = String.Format(CultureInfo.InvariantCulture, "SharedAccessSignature sr={0}&sig={1}&se={2}", 
          WebUtility.UrlEncode(resourceUri), WebUtility.UrlEncode(signature), expiry);          
      return token;
    }

    private string moduleGenerationId;
    private string edgeGatewayHostName;
    private string workloadApiVersion;
    private string workloadUriString;

    public async Task<Boolean> Connect()
    {
      var hostname = System.Environment.GetEnvironmentVariable("IOTEDGE_GATEWAYHOSTNAME");
      DeviceId = System.Environment.GetEnvironmentVariable("IOTEDGE_DEVICEID");
      ModuleId = System.Environment.GetEnvironmentVariable("IOTEDGE_MODULEID");
      IotHubHostName = System.Environment.GetEnvironmentVariable("IOTEDGE_IOTHUBHOSTNAME");

      moduleGenerationId = Environment.GetEnvironmentVariable("IOTEDGE_MODULEGENERATIONID");
      edgeGatewayHostName = Environment.GetEnvironmentVariable("IOTEDGE_GATEWAYHOSTNAME");
      workloadApiVersion = Environment.GetEnvironmentVariable("IOTEDGE_APIVERSION");
      workloadUriString = Environment.GetEnvironmentVariable("IOTEDGE_WORKLOADURI");

      string password = await GetModuleToken(3600);

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