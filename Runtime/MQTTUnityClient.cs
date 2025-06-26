/*
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
using SOSXR.EnhancedLogger;
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
    public class MQTTUnityClient : MonoBehaviour
    {
        [Header("MQTT broker configuration")]
        [Tooltip("IP address or URL of the host running the broker")]
        [SerializeField] protected string BrokerAddress = "localhost";
        [Tooltip("Port where the broker accepts connections")]
        [SerializeField] protected int BrokerPort = 1883;
        [Tooltip("Use encrypted connection")]
        [SerializeField] protected bool IsEncrypted = false;
        [Header("Connection parameters")]
        [Tooltip("Connection to the broker is delayed by the the given milliseconds")]
        [SerializeField] private int m_connectionDelay = 500;
        [Tooltip("Connection timeout in milliseconds")]
        [SerializeField] private int m_timeoutOnConnection = MqttSettings.MQTT_CONNECT_TIMEOUT;
        [Tooltip("Connect on startup")]
        [SerializeField] protected bool AutoConnect = false;
        [Tooltip("UserName for the MQTT broker. Keep blank if no user name is required.")]
        [SerializeField] private string m_mqttUserName = null;
        [Tooltip("Password for the MQTT broker. Keep blank if no password is required.")]
        [SerializeField] private string m_mqttPassword = null;

        [Header("Debugging")]
        [Tooltip("Set this to 1 or higher to perform a testing cycle automatically on startup")]
        [SerializeField] [Range(-1, 10)] private int m_testInterval = 5; // seconds
        [SerializeField] private string m_testTopic = "M2MQTT_Unity/test";
        [SerializeField] private string m_testMessage = "Hello world!";


        /// <summary>
        ///     Wrapped MQTT client
        /// </summary>
        protected MqttClient client;

        private readonly List<MqttMsgPublishEventArgs> _messageQueue1 = new();
        private readonly List<MqttMsgPublishEventArgs> _messageQueue2 = new();
        private List<MqttMsgPublishEventArgs> _frontMessageQueue = null;
        private List<MqttMsgPublishEventArgs> _backMessageQueue = null;
        private bool _connectionClosed = false;
        private bool _connected = false;

        /// <summary>
        ///     Event fired when a connection is successfully established
        /// </summary>
        public event Action ConnectionSucceeded;
        /// <summary>
        ///     Event fired when failing to connect
        /// </summary>
        public event Action ConnectionFailed;

        private static MQTTUnityClient _instance;

        private Coroutine _connect;
        private Coroutine _disconnect;


        private void OnValidate()
        {
            if (m_testInterval > 0)
            {
                AutoConnect = true;
            }
        }


        /// <summary>
        ///     Initialize MQTT message queue
        ///     Remember to call base.Awake() if you override this method.
        /// </summary>
        protected virtual void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);

                return;
            }

            _instance = this;

            _frontMessageQueue = _messageQueue1;
            _backMessageQueue = _messageQueue2;
        }


        private void Start()
        {
            if (AutoConnect)
            {
                Connect();
            }
        }


        [ContextMenu(nameof(Connect))]
        public virtual void Connect()
        {
            if (client is not {IsConnected: true})
            {
                if (_connect != null)
                {
                    StopCoroutine(_connect);
                }

                _connect = StartCoroutine(ConnectCR());
            }
        }


        private IEnumerator ConnectCR()
        {
            yield return new WaitForSecondsRealtime(m_connectionDelay / 1000f);

            yield return new WaitForEndOfFrame(); // leave some time to Unity to refresh the UI

            if (client == null)
            {
                try
                {
                    #if (!UNITY_EDITOR && UNITY_WSA_10_0 && !ENABLE_IL2CPP)
                    client = new MqttClient(brokerAddress,brokerPort,isEncrypted, isEncrypted ? MqttSslProtocols.SSLv3 : MqttSslProtocols.None);
                    #else
                    client = new MqttClient(BrokerAddress, BrokerPort, IsEncrypted, null, null, IsEncrypted ? MqttSslProtocols.SSLv3 : MqttSslProtocols.None);
                    #endif
                }
                catch (Exception e)
                {
                    client = null;

                    this.Error($"Connection to {BrokerAddress}:{BrokerPort} failed: {e.Message}");
                    OnConnectionFailed(e.Message);

                    yield break;
                }
            }
            else if (client.IsConnected)
            {
                yield break;
            }

            OnConnecting();

            yield return new WaitForEndOfFrame(); // leave some time to Unity to refresh the UI
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

                this.Error($"Failed to connect to {BrokerAddress}:{BrokerPort}\n (check client parameters: encryption, address/port, username/password):\n{e}");
                OnConnectionFailed(e.Message);

                yield break;
            }

            if (client.IsConnected)
            {
                client.ConnectionClosed += OnMqttConnectionClosed;
                // register to message received
                client.MqttMsgPublishReceived += OnMqttMessageReceived;
                _connected = true;
                OnConnected();

                RegisterCallbacks();


                ConnectionSucceeded?.Invoke();

                if (m_testInterval > 0)
                {
                    SubscribeToTopic(m_testTopic, null);
                }
            }
            else
            {
                OnConnectionFailed();
                ConnectionFailed?.Invoke();
            }

            _connect = null;
        }


        private void RegisterCallbacks()
        {
            client.MqttMsgPublishReceived += (sender, e) =>
            {
                var topic = e.Topic;
                var payload = Encoding.UTF8.GetString(e.Message);

                if (_topicCallbacks.TryGetValue(topic, out var callbacks))
                {
                    foreach (var callback in callbacks)
                    {
                        try
                        {
                            callback?.Invoke(payload);
                        }
                        catch (Exception ex)
                        {
                            this.Error($"Callback error on topic {topic}: {ex.Message}");
                        }
                    }
                }
                else
                {
                    this.Warning($"No callback registered for topic {topic}, but a message was received.");
                }
            };
        }


        /// <summary>
        ///     Override this method to take some actions before connection (e.g. display a message)
        /// </summary>
        protected virtual void OnConnecting()
        {
            this.Info($"Connecting to broker on {BrokerAddress}:{BrokerPort}...");
        }


        /// <summary>
        ///     Override this method to take some actions if the connection succeeded.
        /// </summary>
        protected virtual void OnConnected()
        {
            this.Success($"Connected to {BrokerAddress}:{BrokerPort}...");

            if (m_testInterval > 0)
            {
                InvokeRepeating(nameof(PublishTestTopic), 0, m_testInterval);
            }
        }


        /// <summary>
        ///     Override this method to take some actions if the connection failed.
        /// </summary>
        /// <param name="errorMessage"></param>
        protected virtual void OnConnectionFailed(string errorMessage = "Unknown error")
        {
            this.Error($"Connection failed to {BrokerAddress}:{BrokerPort}. Error: {errorMessage}");
        }


        private readonly Dictionary<string, List<Action<string>>> _topicCallbacks = new();

/// <summary>
/// Callback can be null
/// </summary>
/// <param name="topic"></param>
/// <param name="callback"></param>
/// <param name="qosLevel"></param>
        public void SubscribeToTopic(string topic, Action<string> callback, byte qosLevel = MqttMsgBase.QOS_LEVEL_EXACTLY_ONCE)
        {
            if (client is not {IsConnected: true})
            {
                this.Error($"Cannot subscribe to topic {topic}, client is not connected.");

                return;
            }

            if (callback != null)
            {
                if (!_topicCallbacks.ContainsKey(topic))
                {
                    _topicCallbacks[topic] = new List<Action<string>>();
                }

                if (!_topicCallbacks[topic].Contains(callback))
                {
                    _topicCallbacks[topic].Add(callback);
                }
            }

            client.Subscribe(new[] {topic}, new[] {qosLevel});
            this.Success($"Subscribed to topic: {topic} with QoS level: {qosLevel}");
        }


        public void UnsubscribeFromTopic(string topic, Action<string> callback = null)
        {
            if (client is not {IsConnected: true})
            {
                this.Error($"Cannot unsubscribe from topic {topic}, client is not connected.");

                return;
            }

            if (callback != null && _topicCallbacks.TryGetValue(topic, out var callbacks))
            {
                callbacks.Remove(callback);

                if (callbacks.Count == 0)
                {
                    _topicCallbacks.Remove(topic);
                    client.Unsubscribe(new[] {topic});
                    this.Success($"Unsubscribed from topic: {topic}");
                }
            }
            else if (callback == null)
            {
                _topicCallbacks.Remove(topic);
                client.Unsubscribe(new[] {topic});
                this.Success($"Unsubscribed from topic: {topic}");
            }
        }


        public void Publish(string topic, string payload, byte qosLevel = MqttMsgBase.QOS_LEVEL_EXACTLY_ONCE, bool retain = false)
        {
            if (client is not {IsConnected: true})
            {
                this.Error($"Cannot publish to topic {topic}, client is not connected.");

                return;
            }

            client.Publish(topic, Encoding.UTF8.GetBytes(payload), qosLevel, retain);
            this.Verbose($"Published message to topic: {topic} with QoS level: {qosLevel} and retain: {retain}");
        }


        public void Publish(string topic, byte[] payload, byte qosLevel = MqttMsgBase.QOS_LEVEL_EXACTLY_ONCE, bool retain = false)
        {
            if (client is not {IsConnected: true})
            {
                this.Error($"Cannot publish to topic {topic}, client is not connected.");

                return;
            }

            client.Publish(topic, payload, qosLevel, retain);
            this.Verbose($"Published message to topic: {topic} with QoS level: {qosLevel} and retain: {retain}");
        }


        /// <summary>
        ///     Override this method for each received message you need to process.
        /// </summary>
        protected virtual void DecodeMessage(string topic, byte[] message)
        {
            this.Verbose("Message received on topic: " + topic + " - " + Encoding.UTF8.GetString(message));
        }


        /// <summary>
        ///     Override this method to take some actions when the connection is closed.
        /// </summary>
        protected virtual void OnConnectionLost()
        {
            this.Error("Connection to broker lost: " + BrokerAddress + ":" + BrokerPort);
        }


        [ContextMenu(nameof(PublishTestTopic))]
        public void PublishTestTopic()
        {
            client.Publish(m_testTopic, Encoding.UTF8.GetBytes(m_testMessage), MqttMsgBase.QOS_LEVEL_EXACTLY_ONCE, false);

            this.Success($"Test message published to topic: {m_testTopic}");
        }


        /// <summary>
        ///     Processing of income messages and events is postponed here in the main thread.
        ///     Remember to call ProcessMqttEvents() in Update() method if you override it.
        /// </summary>
        private void Update()
        {
            ProcessMqttEvents();
        }


        private void ProcessMqttEvents()
        {
            // process messages in the main queue
            SwapMqttMessageQueues();
            ProcessMqttMessageBackgroundQueue();
            // process messages income in the meanwhile
            SwapMqttMessageQueues();
            ProcessMqttMessageBackgroundQueue();

            if (_connectionClosed)
            {
                _connectionClosed = false;
                OnConnectionLost();
            }
        }


        private void ProcessMqttMessageBackgroundQueue()
        {
            foreach (var msg in _backMessageQueue)
            {
                DecodeMessage(msg.Topic, msg.Message);
            }

            _backMessageQueue.Clear();
        }


        /// <summary>
        ///     Swap the message queues to continue receiving message when processing a queue.
        /// </summary>
        private void SwapMqttMessageQueues()
        {
            _frontMessageQueue = _frontMessageQueue == _messageQueue1 ? _messageQueue2 : _messageQueue1;
            _backMessageQueue = _backMessageQueue == _messageQueue1 ? _messageQueue2 : _messageQueue1;
        }


        private void OnMqttMessageReceived(object sender, MqttMsgPublishEventArgs msg)
        {
            _frontMessageQueue.Add(msg);
        }


        private void OnMqttConnectionClosed(object sender, EventArgs e)
        {
            // Set unexpected connection closed only if connected (avoid event handling in case of controlled disconnection)
            _connectionClosed = _connected;
            _connected = false;
        }


        /// <summary>
        ///     Disconnect from the broker, if connected.
        /// </summary>
        [ContextMenu(nameof(Disconnect))]
        public virtual void Disconnect()
        {
            if (client == null)
            {
                return;
            }

            if (_disconnect != null)
            {
                StopCoroutine(_disconnect);
            }

            _disconnect = StartCoroutine(DisconnectCR());
        }


        private IEnumerator DisconnectCR()
        {
            yield return new WaitForEndOfFrame();
            DisconnectImmediately();

            _disconnect = null;
        }


        private void DisconnectImmediately()
        {
            _connected = false;

            if (client == null)
            {
                return;
            }

            if (client.IsConnected)
            {
                if (m_testInterval > 0)
                {
                    CancelInvoke(nameof(PublishTestTopic));
                    UnsubscribeFromTopic(m_testTopic);
                }

                client.Disconnect();
            }
            else
            {
                this.Info("Disconnect called, but client is not connected.");
            }

            client.MqttMsgPublishReceived -= OnMqttMessageReceived;
            client.ConnectionClosed -= OnMqttConnectionClosed;
            client = null;

            OnDisconnected();
        }


        /// <summary>
        ///     Override this method to take some actions when disconnected.
        /// </summary>
        protected virtual void OnDisconnected()
        {
            this.Success("Disconnected from broker: " + BrokerAddress + ":" + BrokerPort);
        }


        /// <summary>
        ///     Disconnect before the application quits.
        /// </summary>
        protected virtual void OnApplicationQuit()
        {
            DisconnectImmediately();
            StopAllCoroutines();
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