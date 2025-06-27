/*
The MIT License (MIT)

Copyright (c) 2018 Giovanni Paolo Vigano' && 2025 SOSXR

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
    public class MQTTClient : MonoBehaviour
    {
        [Header("MQTT broker configuration")]
        [Tooltip("IP address or URL of the host running the broker")]
        [SerializeField] private string m_brokerAddress = "localhost";
        [Tooltip("Port where the broker accepts connections")]
        [SerializeField] private int m_brokerPort = 1883;
        [Tooltip("Use encrypted connection")]
        [SerializeField] private bool m_isEncrypted = false;
        [Header("Connection parameters")]
        [Tooltip("Connection to the broker is delayed by the the given milliseconds")]
        [SerializeField] private int m_connectionDelay = 500;
        [Tooltip("Connection timeout in milliseconds")]
        [SerializeField] private int m_timeoutOnConnection = MqttSettings.MQTT_CONNECT_TIMEOUT;
        [Tooltip("Connect on startup")]
        [SerializeField] private bool m_autoConnect = false;
        [Tooltip("UserName for the MQTT broker. Keep blank if no user name is required.")]
        [SerializeField] private string m_mqttUserName = string.Empty;
        [Tooltip("Password for the MQTT broker. Keep blank if no password is required.")]
        [SerializeField] private string m_mqttPassword = string.Empty;

        [Header("Debugging")]
        [Tooltip("Set this to 1 or higher to perform a testing cycle automatically on startup")]
        [SerializeField]
        protected string m_testTopic = "SOSXR/Test";
        [SerializeField] private string m_testMessage = "Hello world!";

        protected static MQQTBackend MQTTBackend;

        private readonly List<MqttMsgPublishEventArgs> _messageQueue1 = new();
        private readonly List<MqttMsgPublishEventArgs> _messageQueue2 = new();
        private List<MqttMsgPublishEventArgs> _frontMessageQueue = null;
        private List<MqttMsgPublishEventArgs> _backMessageQueue = null;
        private bool _connectionClosed = false;
        private bool _connected = false;

        private static readonly Dictionary<string, List<Action<string, byte[]>>> _topicCallbacks = new();

        private static List<(string topic, byte qosLevel)> _pendingSubscriptions; // For anyone subscribing before we're ready and connected
        private static List<string> _pendingUnsubscriptions;
        private static List<(string topic, byte[] message, byte qosLevel, bool retain)> _pendingPublications;


        public static MQTTClient Instance;

        private Coroutine _connect;
        private Coroutine _disconnect;

        public string BrokerAddress
        {
            get => m_brokerAddress;
            set => m_brokerAddress = value;
        }

        public string BrokerPortString
        {
            get => m_brokerPort.ToString();
            set => m_brokerPort = int.TryParse(value, out var port) ? port : m_brokerPort;
        }

        public int BrokerPort
        {
            get => m_brokerPort;
            set => m_brokerPort = value;
        }

        public bool IsEncrypted
        {
            get => m_isEncrypted;
            set => m_isEncrypted = value;
        }


        public static bool IsConnected => MQTTBackend is {IsConnected: true};
        public static Action OnConnected;
        public static Action OnDisconnected;


        private void OnValidate()
        {
            if (!string.IsNullOrEmpty(m_testTopic))
            {
                m_autoConnect = true;
            }
        }


        /// <summary>
        ///     Initialize MQTT message queue
        /// </summary>
        protected virtual void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);

                return;
            }

            Instance = this;

            _frontMessageQueue = _messageQueue1;
            _backMessageQueue = _messageQueue2;

            if (m_autoConnect)
            {
                Connect();
            }
        }


        private void Start()
        {
            Subscribe(null, m_testTopic); // Subscribe to the test topic with a null callback to ensure the topic is registered
        }


        [ContextMenu(nameof(Connect))]
        public virtual void Connect()
        {
            if (MQTTBackend is {IsConnected: true})
            {
                return;
            }

            if (_connect != null)
            {
                StopCoroutine(_connect);
            }

            _connect = StartCoroutine(ConnectCR());
        }


        private IEnumerator ConnectCR()
        {
            yield return new WaitForSecondsRealtime(m_connectionDelay / 1000f);

            yield return new WaitForEndOfFrame(); // leave some time to Unity to refresh the UI

            if (MQTTBackend == null)
            {
                try
                {
                    #if (!UNITY_EDITOR && UNITY_WSA_10_0 && !ENABLE_IL2CPP)
                    client = new MqttClient(brokerAddress,brokerPort,isEncrypted, isEncrypted ? MqttSslProtocols.SSLv3 : MqttSslProtocols.None);
                    #else
                    MQTTBackend = new MQQTBackend(m_brokerAddress, m_brokerPort, m_isEncrypted, null, null, m_isEncrypted ? MqttSslProtocols.SSLv3 : MqttSslProtocols.None);
                    #endif
                }
                catch (Exception e)
                {
                    MQTTBackend = null;

                    this.Error($"Connection to {m_brokerAddress}:{m_brokerPort} failed: {e.Message}");

                    yield break;
                }
            }
            else if (MQTTBackend.IsConnected)
            {
                yield break;
            }

            yield return new WaitForEndOfFrame(); // leave some time to Unity to refresh the UI
            yield return new WaitForEndOfFrame();

            MQTTBackend.Settings.TimeoutOnConnection = m_timeoutOnConnection;
            var clientId = Guid.NewGuid().ToString();

            try
            {
                MQTTBackend.Connect(clientId, m_mqttUserName, m_mqttPassword);
            }
            catch (Exception e)
            {
                MQTTBackend = null;

                this.Error($"Failed to connect to {m_brokerAddress}:{m_brokerPort}\n (check client parameters: encryption, address/port, username/password):\n{e}");
                OnDisconnected?.Invoke();

                yield break;
            }

            if (MQTTBackend.IsConnected)
            {
                BackendIsConnected();
            }
            else
            {
                OnDisconnected?.Invoke();
                this.Error($"Connection failed to {m_brokerAddress}:{m_brokerPort}");
            }

            _connect = null;
        }


        private void BackendIsConnected()
        {
            MQTTBackend.ConnectionClosed += OnMqttConnectionClosed;
            MQTTBackend.MqttMsgPublishReceived += OnMqttMessageReceived;

            this.Success($"Connected to {m_brokerAddress}:{m_brokerPort}...");

            FlushPendingUnsubscriptions();

            FlushPendingSubscriptions();

            FlushPendingPublications();

            Invoke(nameof(TestPublish), 1f); // delay the test message to ensure the connection is established
            _connected = true;

            OnConnected?.Invoke();
        }


        private void FlushPendingUnsubscriptions()
        {
            if (_pendingUnsubscriptions == null || _pendingUnsubscriptions.Count == 0)
            {
                return;
            }

            foreach (var topic in _pendingUnsubscriptions)
            {
                this.Verbose("Unsubscribing from pending topic: " + topic);
                MQTTBackend.Unsubscribe(new[] {topic});
            }

            this.Info($"Flushed {_pendingUnsubscriptions.Count} pending unsubscriptions.");
            _pendingUnsubscriptions.Clear();
        }


        private void FlushPendingSubscriptions()
        {
            if (_pendingSubscriptions == null || _pendingSubscriptions.Count == 0)
            {
                return;
            }

            foreach (var (topic, qosLevel) in _pendingSubscriptions)
            {
                this.Verbose("Subscribing to pending topic: " + topic + " with QoS level: " + qosLevel);
                MQTTBackend.Subscribe(new[] {topic}, new[] {qosLevel});
            }

            this.Info($"Flushed {_pendingSubscriptions.Count} pending subscriptions.");
            _pendingSubscriptions.Clear();
        }


        private void FlushPendingPublications()
        {
            if (_pendingPublications == null || _pendingPublications.Count == 0)
            {
                return;
            }

            foreach (var (topic, message, qosLevel, retain) in _pendingPublications)
            {
                MQTTBackend.Publish(topic, message, qosLevel, retain);
            }

            this.Verbose($"Flushed {_pendingPublications.Count} pending publications.");
            _pendingPublications.Clear();
        }


        [ContextMenu(nameof(TestPublish))]
        public void TestPublish()
        {
            Publish(m_testTopic, m_testMessage);
        }


        /// <summary>
        ///     Defaults to subscribing to all topics ("#") with QoS level 2 (exactly once).
        /// </summary>
        /// <param name="callback"></param>
        /// <param name="topic"></param>
        /// <param name="qosLevel"></param>
        public static void Subscribe(Action<string, byte[]> callback, string topic = "#", byte qosLevel = MqttMsgBase.QOS_LEVEL_EXACTLY_ONCE)
        {
            if (!_topicCallbacks.TryGetValue(topic, out var callbacks))
            {
                callbacks = new List<Action<string, byte[]>>();
                _topicCallbacks[topic] = callbacks;

                if (MQTTBackend is {IsConnected: true})
                {
                    MQTTBackend.Subscribe(new[] {topic}, new[] {qosLevel});
                    Log.Static($"Subscribed to topic: {topic} with QoS level: {qosLevel}", LogLevel.Verbose);
                }
                else
                {
                    _pendingSubscriptions ??= new List<(string topic, byte qosLevel)>();

                    _pendingSubscriptions.Add((topic, qosLevel)); // Store pending subscriptions to be processed later
                    Log.Static($"Pending subscription to topic: {topic} with QoS level: {qosLevel}", LogLevel.Verbose);
                }
            }
            else
            {
                Log.Static($"Already subscribed to topic: {topic} with QoS level: {qosLevel}", LogLevel.Verbose);
            }


            if (!callbacks.Contains(callback))
            {
                callbacks.Add(callback);
            }
        }


        public static void Unsubscribe(Action<string, byte[]> callback, string topic = "#")
        {
            if (!_topicCallbacks.TryGetValue(topic, out var callbacks))
            {
                return;
            }

            callbacks.Remove(callback);

            if (callbacks.Count != 0)
            {
                return;
            }

            _topicCallbacks.Remove(topic);

            if (MQTTBackend is {IsConnected: true})
            {
                MQTTBackend.Unsubscribe(new[] {topic});
                Log.Static($"Unsubscribed from topic: {topic}", LogLevel.Verbose);
            }
            else
            {
                _pendingUnsubscriptions ??= new List<string>();

                _pendingUnsubscriptions.Add(topic); // Store pending unsubscriptions to be processed later
                Log.Static($"Pending unsubscription from topic: {topic}", LogLevel.Verbose);
            }
        }


        public static void Publish(string topic, string payload, byte qosLevel = MqttMsgBase.QOS_LEVEL_EXACTLY_ONCE, bool retain = false)
        {
            Publish(topic, Encoding.UTF8.GetBytes(payload), qosLevel, retain);
        }


        public static void Publish(string topic, byte[] payload, byte qosLevel = MqttMsgBase.QOS_LEVEL_EXACTLY_ONCE, bool retain = false)
        {
            var mssg = Encoding.UTF8.GetString(payload);

            if (MQTTBackend is {IsConnected: true})
            {
                MQTTBackend.Publish(topic, payload, qosLevel, retain);
                Log.Static($"Published message to topic: {topic} with payload: {mssg} and QoS level: {qosLevel} (retain: {retain})", LogLevel.Verbose);
            }
            else
            {
                _pendingPublications ??= new List<(string topic, byte[] message, byte qosLevel, bool retain)>();

                _pendingPublications.Add((topic, payload, qosLevel, retain)); // Store pending publications to be processed later
                Log.Static($"Pending publication to topic: {topic} with payload: {mssg} and QoS level: {qosLevel} (retain: {retain})", LogLevel.Verbose);
            }
        }


        private void DecodeMessage(string topic, byte[] payload)
        {
            this.Verbose($"Message received on topic: {topic} - {Encoding.UTF8.GetString(payload)}");

            foreach (var (filter, callbacks) in _topicCallbacks)
            {
                if (!TopicMatches(filter, topic))
                {
                    continue;
                }

                foreach (var callback in callbacks)
                {
                    try
                    {
                        callback?.Invoke(topic, payload);
                    }
                    catch (Exception e)
                    {
                        this.Error($"Error in MQTT callback for topic {filter} (received: {topic}): {e}");
                    }
                }
            }
        }


        private static bool TopicMatches(string filter, string topic)
        {
            var filterLevels = filter.Split('/');
            var topicLevels = topic.Split('/');

            for (var i = 0; i < filterLevels.Length; i++)
            {
                if (i >= topicLevels.Length)
                {
                    return false;
                }

                if (filterLevels[i] == "#")
                {
                    return true;
                }

                if (filterLevels[i] == "+")
                {
                    continue;
                }

                if (filterLevels[i] != topicLevels[i])
                {
                    return false;
                }
            }

            return filterLevels.Length == topicLevels.Length;
        }


        /// <summary>
        ///     Processing of income messages and events is postponed here in the main thread.
        /// </summary>
        private void Update()
        {
            ProcessMqttEvents();
        }


        /// <summary>
        ///     Swap twice to process all messages even if new ones came in during the first swap
        /// </summary>
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
                _connectionClosed = true;
                this.Info("Connection to the broker closed.");
                OnDisconnected?.Invoke();
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


        [ContextMenu(nameof(Disconnect))]
        private void Disconnect()
        {
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

            Unsubscribe(null, m_testTopic);

            if (MQTTBackend != null)
            {
                MQTTBackend.Disconnect();
                MQTTBackend.MqttMsgPublishReceived -= OnMqttMessageReceived;
                MQTTBackend.ConnectionClosed -= OnMqttConnectionClosed;
                MQTTBackend = null;
            }

            _frontMessageQueue.Clear();
            _backMessageQueue.Clear();
            _pendingSubscriptions?.Clear();
            _pendingUnsubscriptions?.Clear();
            _pendingPublications?.Clear();

            foreach (var callbacks in _topicCallbacks.Values)
            {
                callbacks.Clear();
            }

            _topicCallbacks.Clear();

            OnDisconnected?.Invoke();

            this.Success("Disconnected from broker: " + m_brokerAddress + ":" + m_brokerPort);
        }


        /// <summary>
        ///     Disconnect before the application quits.
        /// </summary>
        private void OnApplicationQuit()
        {
            StopAllCoroutines();
            DisconnectImmediately();
        }


        #if ((!UNITY_EDITOR && UNITY_WSA_10_0 || !UNITY_EDITOR && UNITY_ANDROID))
        private void OnApplicationFocus(bool focus)
        {
            // On UWP 10 (HoloLeng) / Android we cannot tell whether the application actually got closed or just minimized.
            // (https://forum.unity.com/threads/onapplicationquit-and-ondestroy-are-not-called-on-uwp-10.462597/)
            if (focus)
            {
                Connect();
            }
            else
            {
                DisconnectImmediately();
            }
        }
        #endif
    }
}