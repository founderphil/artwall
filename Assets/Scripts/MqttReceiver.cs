using System;
using System.Text;
using UnityEngine;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Client.Options;

public class MqttReceiver : MonoBehaviour
{
    private IMqttClient mqttClient;

    private string brokerAddress = "artwall.cloud.shiftr.io";
    private int brokerPort = 1883;
    private string username = "artwall";
    private string password = "ES8Z2FND42cLWiCV";
    private string topic1 = "player1/motion";
    private string topic2 = "player2/motion";
    private string topic3 = "player3/motion";

    
    [Serializable]
    public class MotionData
    {
        public int ax; // Accelerometer X
        public int ay; // Accelerometer Y
        public int az; // Accelerometer Z
        public int gx; // Gyroscope X
        public int gy; // Gyroscope Y
        public int gz; // Gyroscope Z
    }

        public MotionData player1MotionData = new MotionData();
        public MotionData player2MotionData = new MotionData();
        public MotionData player3MotionData = new MotionData();

        private float lastAx1, lastAy1, lastAz1;
    private float lastGx1, lastGy1, lastGz1;

    private float lastAx2, lastAy2, lastAz2;
    private float lastGx2, lastGy2, lastGz2;

    private float lastAx3, lastAy3, lastAz3;
    private float lastGx3, lastGy3, lastGz3;
    private const float smoothingFactor = 0.2f; 

    public static MqttReceiver Instance { get; private set; }

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject); 
        }
        else
        {
            Destroy(gameObject);
        }
    }

    private void Start()
    {
        ConnectToMqttBroker();
    }

    private async void ConnectToMqttBroker()
    {
        var factory = new MqttFactory();
        mqttClient = factory.CreateMqttClient();

        var options = new MqttClientOptionsBuilder()
            .WithTcpServer(brokerAddress, brokerPort)
            .WithCredentials(username, password)
            .Build();

        mqttClient.UseConnectedHandler(async e =>
        {
            Debug.Log("Connected to MQTT broker!");
            await mqttClient.SubscribeAsync(topic1);
            await mqttClient.SubscribeAsync(topic2);
            await mqttClient.SubscribeAsync(topic3);
            Debug.Log("Subscribed to player1, player2, player3 topics");
        });

        mqttClient.UseApplicationMessageReceivedHandler(e =>
        {
            string message = Encoding.UTF8.GetString(e.ApplicationMessage.Payload);
            string receivedTopic = e.ApplicationMessage.Topic;

            if (receivedTopic == topic1)
            {
                ProcessMessage(message, ref player1MotionData, ref lastAx1, ref lastAy1, ref lastAz1, ref lastGx1, ref lastGy1, ref lastGz1);
            }
            else if (receivedTopic == topic2)
            {
                ProcessMessage(message, ref player2MotionData, ref lastAx2, ref lastAy2, ref lastAz2, ref lastGx2, ref lastGy2, ref lastGz2);
            }
            else if (receivedTopic == topic3)
            {
                ProcessMessage(message, ref player3MotionData, ref lastAx3, ref lastAy3, ref lastAz3, ref lastGx3, ref lastGy3, ref lastGz3);
            }
        });

        try
        {
            await mqttClient.ConnectAsync(options);
        }
        catch (Exception ex)
        {
            Debug.LogError($"Failed to connect to MQTT broker: {ex.Message}");
        }
    }

    private void ProcessMessage(string message, ref MotionData motionData,
                                ref float lastAx, ref float lastAy, ref float lastAz,
                                ref float lastGx, ref float lastGy, ref float lastGz)
    {
        try
        {
            var newMotionData = JsonUtility.FromJson<MotionData>(message);
            motionData.ax = (int)ApplyLowPassFilter(newMotionData.ax, lastAx);
            motionData.ay = (int)ApplyLowPassFilter(newMotionData.ay, lastAy);
            motionData.az = (int)ApplyLowPassFilter(newMotionData.az, lastAz);
            motionData.gx = (int)ApplyLowPassFilter(newMotionData.gx, lastGx);
            motionData.gy = (int)ApplyLowPassFilter(newMotionData.gy, lastGy);
            motionData.gz = (int)ApplyLowPassFilter(newMotionData.gz, lastGz);

            lastAx = motionData.ax;
            lastAy = motionData.ay;
            lastAz = motionData.az;
            lastGx = motionData.gx;
            lastGy = motionData.gy;
            lastGz = motionData.gz;
        }
        catch (Exception ex)
        {
            Debug.LogError($"Failed to process message: {ex.Message}");
        }
    }

    private float ApplyLowPassFilter(float currentValue, float lastValue)
    {
        return lastValue + (currentValue - lastValue) * smoothingFactor;
    }

    private async void OnApplicationQuit()
    {
        if (mqttClient != null && mqttClient.IsConnected)
        {
            await mqttClient.DisconnectAsync();
        }
    }
}