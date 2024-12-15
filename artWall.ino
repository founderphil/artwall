#include <WiFi.h>
#include <ArduinoMqttClient.h>
#include <Wire.h>

// WiFi and MQTT credentials
const char ssid[] = "*********"; // Wi-Fi network name
const char pass[] = "*********"; // Wi-Fi password

// MQTT Broker details
const char broker[] = "*******";  // shiftr.io broker URL (ex. userName.cloud.shiftr.io)
const char username[] = "*****";  // shiftr.io username  (ex. userName)
const char token[] = "*****";   // shiftr.io token - aka your secret key (ex. b4a3f2b6c7d8e9f0)

#define MPU6050_ADDR 0x68

//player1
//const char topic[] = "player1/motion";
//define SDA_PIN 6
//#define SCL_PIN 7
//#define INT_PIN 8

//player2
//const char topic[] = "player2/motion";
//#define SCL_PIN 8  
//#define SDA_PIN 5  
//#define INT_PIN 3  

//player3
const char topic[] = "player3/motion";
#define SCL_PIN 5 
#define SDA_PIN 2
#define INT_PIN 4

long axOffset = 0, ayOffset = 0, azOffset = 0;
long gxOffset = 0, gyOffset = 0, gzOffset = 0;

WiFiClient client;
MqttClient mqttClient(client);

void setup() {
  Serial.begin(115200);
  Wire.begin(SDA_PIN, SCL_PIN);

  // Connect to WiFi
  Serial.print("Connecting to WiFi...");
  WiFi.begin(ssid, pass);
  while (WiFi.status() != WL_CONNECTED) {
    Serial.print(".");
    delay(500);
  }
  Serial.println("\nWiFi connected!");

  // Connect to MQTT broker
  mqttClient.setUsernamePassword(username, token);
  Serial.print("Connecting to MQTT broker...");
  if (!mqttClient.connect(broker, 1883)) {
    Serial.print("Failed to connect to MQTT broker! Error code: ");
    Serial.println(mqttClient.connectError());
    while (true);
  }
  Serial.println("Connected to MQTT broker!");

  // Initialize MPU-6050
  Wire.begin(SDA_PIN, SCL_PIN);   
  Wire.beginTransmission(MPU6050_ADDR);
  Wire.write(0x6B);  
  Wire.write(0);
  Wire.endTransmission();

  // Initialize INT pin
  pinMode(INT_PIN, INPUT);
  Serial.println("MPU-6050 and INT pin initialized!");

  Serial.println("Starting Calibration... Please keep the sensor stationary.");

  // CALIBRATE THE SENSOR
  // Collect 100 readings and calculate the average
  for (int i = 0; i < 100; i++) {
    Wire.beginTransmission(MPU6050_ADDR);
    Wire.write(0x3B);
    Wire.endTransmission(false);
    Wire.requestFrom(MPU6050_ADDR, 14, true);

    int16_t ax = (Wire.read() << 8) | Wire.read();
    int16_t ay = (Wire.read() << 8) | Wire.read();
    int16_t az = (Wire.read() << 8) | Wire.read();

    int16_t gx = (Wire.read() << 8) | Wire.read();
    int16_t gy = (Wire.read() << 8) | Wire.read();
    int16_t gz = (Wire.read() << 8) | Wire.read();

    axOffset += ax;
    ayOffset += ay;
    azOffset += az;
    gxOffset += gx;
    gyOffset += gy;
    gzOffset += gz;

    delay(50); 
  }

  // Calculate average offsets
  axOffset /= 100;
  ayOffset /= 100;
  azOffset /= 100;
  gxOffset /= 100;
  gyOffset /= 100;
  gzOffset /= 100;

  // Print offsets
  Serial.println("Calibration Complete!");
  Serial.print("Accel Offsets: axOffset="); Serial.print(axOffset);
  Serial.print(", ayOffset="); Serial.print(ayOffset);
  Serial.print(", azOffset="); Serial.println(azOffset);
  Serial.print("Gyro Offsets: gxOffset="); Serial.print(gxOffset);
  Serial.print(", gyOffset="); Serial.print(gyOffset);
  Serial.print(", gzOffset="); Serial.println(gzOffset);
  delay(3000);
}

void loop() {
  mqttClient.poll();  
  Wire.beginTransmission(MPU6050_ADDR);
  Wire.write(0x3B);
  Wire.endTransmission(false);
  Wire.requestFrom(MPU6050_ADDR, 14, true);

  // Read accelerometer data
  int16_t ax = (Wire.read() << 8 | Wire.read());
  int16_t ay = (Wire.read() << 8 | Wire.read());
  int16_t az = (Wire.read() << 8 | Wire.read());

  // Read gyroscope data
  int16_t gx = (Wire.read() << 8 | Wire.read());
  int16_t gy = (Wire.read() << 8 | Wire.read());
  int16_t gz = (Wire.read() << 8 | Wire.read());

 // Subtract offsets from raw readings
  int correctedAx = ax - axOffset;
  int correctedAy = ay - ayOffset;
  int correctedAz = az - azOffset;

  int correctedGx = gx - gxOffset;
  int correctedGy = gy - gyOffset;
  int correctedGz = gz - gzOffset;

  // Package corrected values into JSON into a payload to send to MQTT broker
  String payload = "{";
  payload += "\"ax\":" + String(correctedAx) + ",";
  payload += "\"ay\":" + String(correctedAy) + ",";
  payload += "\"az\":" + String(correctedAz) + ",";
  payload += "\"gx\":" + String(correctedGx) + ",";
  payload += "\"gy\":" + String(correctedGy) + ",";
  payload += "\"gz\":" + String(correctedGz);
  payload += "}";

  // Publish the message
  Serial.print("Sending: ");
  Serial.println(payload);

  mqttClient.beginMessage(topic);
  mqttClient.print(payload);
  mqttClient.endMessage();

  delay(1);  // Publish data every 1ms
}