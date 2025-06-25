﻿/*
The MIT License (MIT)

Copyright (c) 2018 Giovanni Paolo Vigano'

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
*/

using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using uPLibrary.Networking.M2Mqtt;
using uPLibrary.Networking.M2Mqtt.Messages;


/// <summary>
/// Adaptation for Unity of the M2MQTT library (https://github.com/eclipse/paho.mqtt.m2mqtt),
/// modified to run on UWP (also tested on Microsoft HoloLens).
/// </summary>
namespace M2MqttUnity
{
    /// <summary>
    ///     Generic MonoBehavior wrapping a MQTT client, using a double buffer to postpone message processing in the main thread.
    /// </summary>
    public class M2MqttUnityClient : MonoBehaviour
    {
        [Header("MQTT broker configuration")]
        [Tooltip("IP address or URL of the host running the broker")]
        [SerializeField] protected string m_brokerAddress = "localhost";
        [Tooltip("Port where the broker accepts connections")]
        [SerializeField] protected int m_brokerPort = 1883;
        [Tooltip("Use encrypted connection")]
        [SerializeField] protected bool m_isEncrypted = false;
        [Header("Connection parameters")]
        [Tooltip("Connection to the broker is delayed by the the given milliseconds")]
        [SerializeField] private int m_connectionDelay = 500;
        [Tooltip("Connection timeout in milliseconds")]
        [SerializeField] private int m_timeoutOnConnection = MqttSettings.MQTT_CONNECT_TIMEOUT;
        [Tooltip("Connect on startup")]
        [SerializeField] protected bool m_autoConnect = false;
        [Tooltip("UserName for the MQTT broker. Keep blank if no user name is required.")]
        [SerializeField] private string m_mqttUserName = null;
        [Tooltip("Password for the MQTT broker. Keep blank if no password is required.")]
        [SerializeField] private string m_mqttPassword = null;

        [Header("Debugging")]
        [Tooltip("Set this to true to perform a testing cycle automatically on startup")]
        [SerializeField] private bool m_autoTest = false;
        [SerializeField] private int m_testInterval = 5; // seconds
        [SerializeField] private string m_testTopic = "M2MQTT_Unity/test";
        [SerializeField] private string m_testMessage = "Hello world!";


        /// <summary>
        ///     Wrapped MQTT client
        /// </summary>
        protected MqttClient client;

        private readonly List<MqttMsgPublishEventArgs> messageQueue1 = new();
        private readonly List<MqttMsgPublishEventArgs> messageQueue2 = new();
        private List<MqttMsgPublishEventArgs> frontMessageQueue = null;
        private List<MqttMsgPublishEventArgs> backMessageQueue = null;
        private bool mqttClientConnectionClosed = false;
        private bool mqttClientConnected = false;

        /// <summary>
        ///     Event fired when a connection is successfully established
        /// </summary>
        public event Action ConnectionSucceeded;
        /// <summary>
        ///     Event fired when failing to connect
        /// </summary>
        public event Action ConnectionFailed;


        private void OnValidate()
        {
            if (m_autoTest)
            {
                m_autoConnect = true;
            }
        }


        /// <summary>
        ///     Connect to the broker using current settings.
        /// </summary>
        public virtual void Connect()
        {
            if (client == null || !client.IsConnected)
            {
                StartCoroutine(DoConnect());
            }
        }


        /// <summary>
        ///     Disconnect from the broker, if connected.
        /// </summary>
        public virtual void Disconnect()
        {
            if (client != null)
            {
                StartCoroutine(DoDisconnect());
            }
        }


        /// <summary>
        ///     Override this method to take some actions before connection (e.g. display a message)
        /// </summary>
        protected virtual void OnConnecting()
        {
            Debug.LogFormat("Connecting to broker on {0}:{1}...\n", m_brokerAddress, m_brokerPort.ToString());
        }


        /// <summary>
        ///     Override this method to take some actions if the connection succeeded.
        /// </summary>
        protected virtual void OnConnected()
        {
            Debug.LogFormat("Connected to {0}:{1}...\n", m_brokerAddress, m_brokerPort.ToString());

            SubscribeTopics();

            ConnectionSucceeded?.Invoke();

            if (m_autoTest)
            {
                InvokeRepeating(nameof(TestPublish), 0, m_testInterval);
            }
        }


        [ContextMenu(nameof(TestPublish))]
        public void TestPublish()
        {
            client.Publish(m_testTopic, Encoding.UTF8.GetBytes(m_testMessage), MqttMsgBase.QOS_LEVEL_EXACTLY_ONCE, false);
            Debug.Log("Test message published to topic: " + m_testTopic);
        }


        /// <summary>
        ///     Override this method to take some actions if the connection failed.
        /// </summary>
        protected virtual void OnConnectionFailed(string errorMessage)
        {
            Debug.LogWarning("Connection failed.");

            ConnectionFailed?.Invoke();
        }


        /// <summary>
        ///     Override this method to subscribe to MQTT topics.
        /// </summary>
        protected virtual void SubscribeTopics()
        {
            client.Subscribe(new[] {m_testTopic}, new[] {MqttMsgBase.QOS_LEVEL_EXACTLY_ONCE});
        }


        /// <summary>
        ///     Override this method to unsubscribe to MQTT topics (they should be the same you subscribed to with SubscribeTopics() ).
        /// </summary>
        protected virtual void UnsubscribeTopics()
        {
            client.Unsubscribe(new[] {m_testTopic});
        }


        /// <summary>
        ///     Disconnect before the application quits.
        /// </summary>
        protected virtual void OnApplicationQuit()
        {
            CloseConnection();
        }


        /// <summary>
        ///     Initialize MQTT message queue
        ///     Remember to call base.Awake() if you override this method.
        /// </summary>
        protected virtual void Awake()
        {
            frontMessageQueue = messageQueue1;
            backMessageQueue = messageQueue2;
        }


        /// <summary>
        ///     Connect on startup if autoConnect is set to true.
        /// </summary>
        protected virtual void Start()
        {
            if (m_autoConnect)
            {
                Connect();
            }
        }


        /// <summary>
        ///     Override this method for each received message you need to process.
        /// </summary>
        protected virtual void DecodeMessage(string topic, byte[] message)
        {
            Debug.LogFormat("Message received on topic: {0}", topic);
        }


        /// <summary>
        ///     Override this method to take some actions when disconnected.
        /// </summary>
        protected virtual void OnDisconnected()
        {
            Debug.Log("Disconnected.");
        }


        /// <summary>
        ///     Override this method to take some actions when the connection is closed.
        /// </summary>
        protected virtual void OnConnectionLost()
        {
            Debug.LogWarning("CONNECTION LOST!");
        }


        /// <summary>
        ///     Processing of income messages and events is postponed here in the main thread.
        ///     Remember to call ProcessMqttEvents() in Update() method if you override it.
        /// </summary>
        protected virtual void Update()
        {
            ProcessMqttEvents();
        }


        protected virtual void ProcessMqttEvents()
        {
            // process messages in the main queue
            SwapMqttMessageQueues();
            ProcessMqttMessageBackgroundQueue();
            // process messages income in the meanwhile
            SwapMqttMessageQueues();
            ProcessMqttMessageBackgroundQueue();

            if (mqttClientConnectionClosed)
            {
                mqttClientConnectionClosed = false;
                OnConnectionLost();
            }
        }


        private void ProcessMqttMessageBackgroundQueue()
        {
            foreach (var msg in backMessageQueue)
            {
                DecodeMessage(msg.Topic, msg.Message);
            }

            backMessageQueue.Clear();
        }


        /// <summary>
        ///     Swap the message queues to continue receiving message when processing a queue.
        /// </summary>
        private void SwapMqttMessageQueues()
        {
            frontMessageQueue = frontMessageQueue == messageQueue1 ? messageQueue2 : messageQueue1;
            backMessageQueue = backMessageQueue == messageQueue1 ? messageQueue2 : messageQueue1;
        }


        private void OnMqttMessageReceived(object sender, MqttMsgPublishEventArgs msg)
        {
            frontMessageQueue.Add(msg);
        }


        private void OnMqttConnectionClosed(object sender, EventArgs e)
        {
            // Set unexpected connection closed only if connected (avoid event handling in case of controlled disconnection)
            mqttClientConnectionClosed = mqttClientConnected;
            mqttClientConnected = false;
        }


        /// <summary>
        ///     Connects to the broker using the current settings.
        /// </summary>
        /// <returns>The execution is done in a coroutine.</returns>
        private IEnumerator DoConnect()
        {
            // wait for the given delay
            yield return new WaitForSecondsRealtime(m_connectionDelay / 1000f);

            // leave some time to Unity to refresh the UI
            yield return new WaitForEndOfFrame();

            // create client instance
            if (client == null)
            {
                try
                {
                    #if (!UNITY_EDITOR && UNITY_WSA_10_0 && !ENABLE_IL2CPP)
                    client = new MqttClient(brokerAddress,brokerPort,isEncrypted, isEncrypted ? MqttSslProtocols.SSLv3 : MqttSslProtocols.None);
                    #else
                    client = new MqttClient(m_brokerAddress, m_brokerPort, m_isEncrypted, null, null, m_isEncrypted ? MqttSslProtocols.SSLv3 : MqttSslProtocols.None);
                    //System.Security.Cryptography.X509Certificates.X509Certificate cert = new System.Security.Cryptography.X509Certificates.X509Certificate();
                    //client = new MqttClient(brokerAddress, brokerPort, isEncrypted, cert, null, MqttSslProtocols.TLSv1_0, MyRemoteCertificateValidationCallback);
                    #endif
                }
                catch (Exception e)
                {
                    client = null;
                    Debug.LogErrorFormat("CONNECTION FAILED! {0}", e);
                    OnConnectionFailed(e.Message);

                    yield break;
                }
            }
            else if (client.IsConnected)
            {
                yield break;
            }

            OnConnecting();

            // leave some time to Unity to refresh the UI
            yield return new WaitForEndOfFrame();
            yield return new WaitForEndOfFrame();

            client.Settings.TimeoutOnConnection = m_timeoutOnConnection;
            var clientId = Guid.NewGuid().ToString();

            try
            {
                client.Connect(clientId, m_mqttUserName, m_mqttPassword);
            }
            catch (Exception e)
            {
                client = null;
                Debug.LogErrorFormat("Failed to connect to {0}:{1}\n (check client parameters: encryption, address/port, username/password):\n{2}", m_brokerAddress, m_brokerPort, e);
                OnConnectionFailed(e.Message);

                yield break;
            }

            if (client.IsConnected)
            {
                client.ConnectionClosed += OnMqttConnectionClosed;
                // register to message received
                client.MqttMsgPublishReceived += OnMqttMessageReceived;
                mqttClientConnected = true;
                OnConnected();
            }
            else
            {
                OnConnectionFailed("CONNECTION FAILED!");
            }
        }


        private IEnumerator DoDisconnect()
        {
            yield return new WaitForEndOfFrame();
            CloseConnection();
            OnDisconnected();
        }


        private void CloseConnection()
        {
            mqttClientConnected = false;

            if (client != null)
            {
                if (client.IsConnected)
                {
                    UnsubscribeTopics();
                    client.Disconnect();
                }

                client.MqttMsgPublishReceived -= OnMqttMessageReceived;
                client.ConnectionClosed -= OnMqttConnectionClosed;
                client = null;
            }
        }


        #if ((!UNITY_EDITOR && UNITY_WSA_10_0))
        private void OnApplicationFocus(bool focus)
        {
            // On UWP 10 (HoloLens) we cannot tell whether the application actually got closed or just minimized.
            // (https://forum.unity.com/threads/onapplicationquit-and-ondestroy-are-not-called-on-uwp-10.462597/)
            if (focus)
            {
                Connect();
            }
            else
            {
                CloseConnection();
            }
        }
        #endif
    }
}