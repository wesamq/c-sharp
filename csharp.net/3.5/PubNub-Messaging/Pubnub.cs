﻿//Build Date: September 13, 2013
#if (UNITY_STANDALONE || UNITY_WEBPLAYER || UNITY_IOS || UNITY_ANDROID)
#define USE_JSONFX
#endif
#if (__MonoCS__ && !UNITY_STANDALONE && !UNITY_WEBPLAYER)
#define TRACE
#endif
#if (UNITY_STANDALONE || UNITY_WEBPLAYER || UNITY_IOS || UNITY_ANDROID)
using UnityEngine;
using System.Security.Cryptography.X509Certificates;
#endif
using System;
using System.IO;
using System.Text;
using System.Net;
using System.Collections;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.ComponentModel;
using System.Reflection;
using System.Threading;
using System.Diagnostics;
using System.Collections.Concurrent;
using System.Globalization;
#if !UNITY_WEBPLAYER
using System.Net.NetworkInformation;
#endif
using System.Net.Sockets;
using System.Configuration;
using Microsoft.Win32;
using System.Linq;
using System.Text.RegularExpressions;
using System.Runtime.Serialization;
#if (SILVERLIGHT || WINDOWS_PHONE)
using System.Windows.Threading;
using System.IO.IsolatedStorage;
using System.Net.Browser;
#endif
#if (__MonoCS__)
using System.Net.Security;
#endif

#if(MONODROID || __ANDROID__)
using Android.Runtime;
using Javax.Net.Ssl;
#endif
#if(MONODROID || __ANDROID__ || UNITY_ANDROID)
using System.Security.Cryptography.X509Certificates;
#endif

#if (USE_JSONFX)
using JsonFx.Json;
#elif (USE_DOTNET_SERIALIZATION)
using System.Runtime.Serialization.Json;
using System.Web.Script.Serialization;
#else
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
#endif

namespace PubNubMessaging.Core
{
	// INotifyPropertyChanged provides a standard event for objects to notify clients that one of its properties has changed
	public class Pubnub : INotifyPropertyChanged
	{
		int _pubnubWebRequestCallbackIntervalInSeconds = 310;
		int _pubnubOperationTimeoutIntervalInSeconds = 15;
		int _pubnubNetworkTcpCheckIntervalInSeconds = 15;
		int _pubnubNetworkCheckRetries = 50;
		int _pubnubWebRequestRetryIntervalInSeconds = 10;
		bool _enableResumeOnReconnect = true;
		bool overrideTcpKeepAlive = true;
		bool _enableJsonEncodingForPublish = true;
		const LoggingMethod.Level pubnubLogLevel = LoggingMethod.Level.Error;
		
#if (!SILVERLIGHT && !WINDOWS_PHONE)
		bool pubnubEnableProxyConfig = true;
#endif
		
		// Common property changed event
		public event PropertyChangedEventHandler PropertyChanged;
		public void RaisePropertyChanged(string propertyName)
		{
			var handler = PropertyChanged;
			if (handler != null)
			{
				handler(this, new PropertyChangedEventArgs(propertyName));
			}
		}
		
		ConcurrentDictionary<string, long> _multiChannelSubscribe = new ConcurrentDictionary<string, long>();
		ConcurrentDictionary<string, PubnubWebRequest> _channelRequest = new ConcurrentDictionary<string, PubnubWebRequest>();
		ConcurrentDictionary<string, bool> _channelInternetStatus = new ConcurrentDictionary<string, bool>();
		ConcurrentDictionary<string, int> _channelInternetRetry = new ConcurrentDictionary<string, int>();
		ConcurrentDictionary<string, Timer> _channelReconnectTimer = new ConcurrentDictionary<string, Timer>();
		ConcurrentDictionary<Uri, Timer> _channelHeartbeatTimer = new ConcurrentDictionary<Uri, Timer>();
        ConcurrentDictionary<string, object> _channelCallbacks = new ConcurrentDictionary<string, object>();
		
		internal int SubscribeTimeout
		{
			get
			{
				return _pubnubWebRequestCallbackIntervalInSeconds;
			}
			
			set
			{
				_pubnubWebRequestCallbackIntervalInSeconds = value;
			}
		}
		
		internal int NonSubscribeTimeout
		{
			get
			{
				return _pubnubOperationTimeoutIntervalInSeconds;
			}
			
			set
			{
				_pubnubOperationTimeoutIntervalInSeconds = value;
			}
		}
		
		internal int NetworkCheckMaxRetries
		{
			get
			{
				return _pubnubNetworkCheckRetries;
			}
			
			set
			{
				_pubnubNetworkCheckRetries = value;
			}
		}
		
		internal int NetworkCheckRetryInterval
		{
			get
			{
				return _pubnubWebRequestRetryIntervalInSeconds;
			}
			
			set
			{
				_pubnubWebRequestRetryIntervalInSeconds = value;
			}
		}
		
		internal int HeartbeatInterval
		{
			get
			{
				return _pubnubNetworkTcpCheckIntervalInSeconds;
			}
			
			set
			{
				_pubnubNetworkTcpCheckIntervalInSeconds = value;
			}
		}
		
		internal bool EnableResumeOnReconnect
		{
			get
			{
				return _enableResumeOnReconnect;
			}
			set
			{
				_enableResumeOnReconnect = value;
			}
		}
		
		public bool EnableJsonEncodingForPublish
		{
			get
			{
				return _enableJsonEncodingForPublish;
			}
			set
			{
				_enableJsonEncodingForPublish = value;
			}
		}

        private string _authenticationKey = "";
        public string AuthenticationKey
        {
            get
            {
                return _authenticationKey;
            }

            set
            {
                _authenticationKey = value;
            }
        }
		
		private IPubnubUnitTest _pubnubUnitTest;
		public IPubnubUnitTest PubnubUnitTest
		{
			get
			{
				return _pubnubUnitTest;
			}
			set
			{
				_pubnubUnitTest = value;
			}
		}
		
		private IJsonPluggableLibrary _jsonPluggableLibrary = null;
		public IJsonPluggableLibrary JsonPluggableLibrary
		{
			get
			{
				return _jsonPluggableLibrary;
			}
			set
			{
				_jsonPluggableLibrary = value;
				if (_jsonPluggableLibrary is IJsonPluggableLibrary)
				{
					ClientNetworkStatus.JsonPluggableLibrary = _jsonPluggableLibrary;
				}
				else
				{
					_jsonPluggableLibrary = null;
					throw new ArgumentException("Missing or Incorrect JsonPluggableLibrary value");
				}
			}
		}
		
#if (!SILVERLIGHT && !WINDOWS_PHONE)
		//Proxy
		private PubnubProxy _pubnubProxy = null;
		public PubnubProxy Proxy
		{
			get
			{
				return _pubnubProxy;
			}
			set
			{
				_pubnubProxy = value;
				if (_pubnubProxy == null)
				{
					throw new ArgumentException("Missing Proxy Details");
				}
				if (string.IsNullOrEmpty(_pubnubProxy.ProxyServer) || (_pubnubProxy.ProxyPort <= 0) || string.IsNullOrEmpty(_pubnubProxy.ProxyUserName) || string.IsNullOrEmpty(_pubnubProxy.ProxyPassword))
				{
					_pubnubProxy = null;
					throw new MissingFieldException("Insufficient Proxy Details");
				}
			}
		}
#endif
		
		System.Threading.Timer heartBeatTimer;
		
		private static bool _pubnetSystemActive = true;
		
		// History of Messages (Obsolete)
		private List<object> _history = new List<object>();
		public List<object> History { get { return _history; } set { _history = value; RaisePropertyChanged("History"); } }
		
		private static long lastSubscribeTimetoken = 0;
		
		// Pubnub Core API implementation
		//private string _origin = "pubsub.pubnub.com";
        private string _origin = "pam-beta.pubnub.com"; //"pres-beta.pubnub.com";//"50.112.215.116";//"pam-beta.pubnub.com"; //;"uls-test.pubnub.co"; //"pam-beta.pubnub.com";
        public string Origin
        {
            get
            {
                return _origin;
            }
            set
            {
                _origin = value;
            }
        }

#if (__MonoCS__)
		private string domainName = "pubsub.pubnub.com";
#endif
		private string publishKey = "";
		private string subscribeKey = "";
		private string secretKey = "";
		private string cipherKey = "";
		private bool ssl = false;
		private string sessionUUID = "";
		private string parameters = "";
		
		public string SessionUUID
		{
			get
			{
				return sessionUUID;
			}
			set
			{
				sessionUUID = value;
			}
		}
		
		/**
         * Pubnub instance initialization function
         * 
         * @param string publishKey.
         * @param string subscribeKey.
         * @param string secretKey.
         * @param bool sslOn
         */
		private void Init(string publishKey, string subscribeKey, string secretKey, string cipherKey, bool sslOn)
		{
#if (USE_JSONFX)
			this.JsonPluggableLibrary = new JsonFXDotNet();
#elif (USE_DOTNET_SERIALIZATION)
			this.JsonPluggableLibrary = new JscriptSerializer();    
#else
			this.JsonPluggableLibrary = new NewtonsoftJsonDotNet();
#endif

#if(MONOTOUCH || __IOS__ || MONODROID || __ANDROID__ || SILVERLIGHT || WINDOWS_PHONE || UNITY_STANDALONE || UNITY_WEBPLAYER || UNITY_IOS || UNITY_ANDROID)
			LoggingMethod.LogLevel = pubnubLogLevel;
#else
            string configuredLogLevel = ConfigurationManager.AppSettings["PubnubMessaging.LogLevel"];
			int logLevelValue;
			if (!Int32.TryParse(configuredLogLevel, out logLevelValue))
			{
				LoggingMethod.LogLevel = pubnubLogLevel;
			}
			else
			{
				LoggingMethod.LogLevel = (LoggingMethod.Level)logLevelValue;
			}
#endif
			
#if (SILVERLIGHT || WINDOWS_PHONE)
			HttpWebRequest.RegisterPrefix("https://", WebRequestCreator.ClientHttp);
			HttpWebRequest.RegisterPrefix("http://", WebRequestCreator.ClientHttp);
#endif
			this.publishKey = publishKey;
			this.subscribeKey = subscribeKey;
			this.secretKey = secretKey;
			this.cipherKey = cipherKey;
			this.ssl = sslOn;
			
			VerifyOrSetSessionUUID();
			
			// SSL is ON?
			if (this.ssl)
				this._origin = "https://" + this._origin;
			else
				this._origin = "http://" + this._origin;
			
#if(UNITY_ANDROID)
			ServicePointManager.ServerCertificateValidationCallback = ValidatorUnity;
#endif
			
			//Initiate System Events for PowerModeChanged - to monitor suspend/resume
			InitiatePowerModeCheck();
		}
		
		private void ReconnectNetwork<T>(ReconnectState<T> netState)
		{
			System.Threading.Timer timer = new Timer(new TimerCallback(ReconnectNetworkCallback<T>), netState, 0,
			                                         (-1 == _pubnubNetworkTcpCheckIntervalInSeconds) ? Timeout.Infinite : _pubnubNetworkTcpCheckIntervalInSeconds * 1000);
            if (netState != null && netState.Channels != null)
            {
                _channelReconnectTimer.AddOrUpdate(string.Join(",", netState.Channels), timer, (key, oldState) => timer);
            }
		}
		
		void ReconnectNetworkCallback<T>(System.Object reconnectState)
		{
			string channel = "";
			
			ReconnectState<T> netState = reconnectState as ReconnectState<T>;
			try
			{
                if (netState != null && netState.Channels != null)
				{
					channel = string.Join(",", netState.Channels);
					
					if (_channelInternetStatus.ContainsKey(channel)
					    && (netState.Type == ResponseType.Subscribe || netState.Type == ResponseType.Presence))
					{
						if (_channelInternetStatus[channel])
						{
							//Reset Retry if previous state is true
							_channelInternetRetry.AddOrUpdate(channel, 0, (key, oldValue) => 0);
						}
						else
						{
							_channelInternetRetry.AddOrUpdate(channel, 1, (key, oldValue) => oldValue + 1);
							LoggingMethod.WriteToLog(string.Format("DateTime {0}, {1} {2} reconnectNetworkCallback. Retry {3} of {4}", DateTime.Now.ToString(), channel, netState.Type, _channelInternetRetry[channel], _pubnubNetworkCheckRetries), LoggingMethod.LevelInfo);

                            if (netState.Channels != null)
                            {
                                for (int index = 0; index < netState.Channels.Length; index++)
                                {
                                    string activeChannel = netState.Channels[index].ToString();

                                    string message = string.Format("Detected internet connection problem. Retrying connection attempt {0} of {1}", _channelInternetRetry[channel], _pubnubNetworkCheckRetries);

                                    if (_channelCallbacks.Count > 0 && _channelCallbacks.ContainsKey(activeChannel))
                                    {
                                        PubnubChannelCallback<T> currentPubnubCallback = _channelCallbacks[activeChannel] as PubnubChannelCallback<T>;
                                        if (currentPubnubCallback != null && currentPubnubCallback.ErrorCallback != null)
                                        {
                                            PubnubErrorCode errorType = PubnubErrorCode.NoInternet;
                                            int statusCode = (int)errorType;
                                            string errorDescription = PubnubErrorCodeDescription.GetStatusCodeDescription(errorType);

                                            PubnubClientError error = new PubnubClientError(statusCode, PubnubErrorSeverity.Info, message, PubnubMessageSource.Client, null, null, errorDescription, activeChannel);
                                            GoToCallback(error, currentPubnubCallback.ErrorCallback);
                                        }
                                    }
                                }
                            }
						}
					}
					
					if (_channelInternetStatus[channel])
					{
						if (_channelReconnectTimer.ContainsKey(channel))
						{
							_channelReconnectTimer[channel].Change(Timeout.Infinite, Timeout.Infinite);
							_channelReconnectTimer[channel].Dispose();
						}
                        string multiChannel = (netState.Channels != null) ? string.Join(",", netState.Channels) : "";
						//List<object> result = new List<object>();
						string message = "Internet connection available";
						//result = _jsonPluggableLibrary.DeserializeToListOfObject(message);
						//result.Add(string.Join(",", netState.Channels));
                        PubnubErrorCode errorType = PubnubErrorCode.YesInternet;
                        int statusCode = (int)errorType;
                        string errorDescription = PubnubErrorCodeDescription.GetStatusCodeDescription(errorType);
                        PubnubClientError error = new PubnubClientError(statusCode, PubnubErrorSeverity.Info, message, PubnubMessageSource.Client, null, null, errorDescription, multiChannel);
                        GoToCallback(error, netState.ErrorCallback);
						
						LoggingMethod.WriteToLog(string.Format("DateTime {0}, {1} {2} reconnectNetworkCallback. Internet Available : {3}", DateTime.Now.ToString(), channel, netState.Type, _channelInternetStatus[channel]), LoggingMethod.LevelInfo);
						switch (netState.Type)
						{
						case ResponseType.Subscribe:
						case ResponseType.Presence:
							MultiChannelSubscribeRequest<T>(netState.Type, netState.Channels, netState.Timetoken, netState.Callback, netState.ConnectCallback, netState.ErrorCallback, true);
							break;
						default:
							break;
						}
					}
					else if (_channelInternetRetry[channel] >= _pubnubNetworkCheckRetries)
					{
						if (_channelReconnectTimer.ContainsKey(channel))
						{
							_channelReconnectTimer[channel].Change(Timeout.Infinite, Timeout.Infinite);
							_channelReconnectTimer[channel].Dispose();
						}
						switch (netState.Type)
						{
						case ResponseType.Subscribe:
						case ResponseType.Presence:
							MultiplexExceptionHandler(netState.Type, netState.Channels, netState.Callback, netState.ConnectCallback, netState.ErrorCallback, true, false);
							break;
						default:
							break;
						}
					}
				}
				else
				{
					LoggingMethod.WriteToLog(string.Format("DateTime {0}, Unknown request state in reconnectNetworkCallback", DateTime.Now.ToString()), LoggingMethod.LevelError);
				}
			}
			catch (Exception ex)
			{
				if (netState != null)
				{
					//TODO: Identify refactoring
                    string multiChannel = (netState.Channels != null) ? string.Join(",", netState.Channels) : "";

                    PubnubErrorCode errorType = PubnubErrorCodeHelper.GetErrorType(ex);
                    int statusCode = (int)errorType;
                    string errorDescription = PubnubErrorCodeDescription.GetStatusCodeDescription(errorType);
                    PubnubClientError error = new PubnubClientError(statusCode, PubnubErrorSeverity.Warn, true, ex.Message, ex, PubnubMessageSource.Client, null, null, errorDescription, multiChannel);
                    GoToCallback(error, netState.ErrorCallback);
				}
				
				LoggingMethod.WriteToLog(string.Format("DateTime {0} method:reconnectNetworkCallback \n Exception Details={1}", DateTime.Now.ToString(), ex.ToString()), LoggingMethod.LevelError);
			}
		}
		
		
		private void InitiatePowerModeCheck()
        {
#if (!SILVERLIGHT && !WINDOWS_PHONE && !MONOTOUCH && !__IOS__ && !MONODROID && !__ANDROID__ && !UNITY_STANDALONE && !UNITY_WEBPLAYER && !UNITY_IOS && !UNITY_ANDROID)
            try
			{
				SystemEvents.PowerModeChanged += new PowerModeChangedEventHandler(SystemEvents_PowerModeChanged);
				LoggingMethod.WriteToLog(string.Format("DateTime {0}, Initiated System Event - PowerModeChanged.", DateTime.Now.ToString()), LoggingMethod.LevelInfo);
			}
			catch (Exception ex)
			{
				LoggingMethod.WriteToLog(string.Format("DateTime {0} No support for System Event - PowerModeChanged.", DateTime.Now.ToString()), LoggingMethod.LevelError);
				LoggingMethod.WriteToLog(string.Format("DateTime {0} {1}", DateTime.Now.ToString(), ex.ToString()), LoggingMethod.LevelError);
			}
#endif
		}

#if (!SILVERLIGHT && !WINDOWS_PHONE && !MONOTOUCH && !__IOS__ && !MONODROID && !__ANDROID__ && !UNITY_STANDALONE && !UNITY_WEBPLAYER && !UNITY_IOS && !UNITY_ANDROID)
        void SystemEvents_PowerModeChanged(object sender, PowerModeChangedEventArgs e)
		{
			if (e.Mode == PowerModes.Suspend)
			{
				_pubnetSystemActive = false;
				TerminatePendingWebRequest();
				if (overrideTcpKeepAlive)
				{
					heartBeatTimer.Change(Timeout.Infinite, Timeout.Infinite);
				}
				
				LoggingMethod.WriteToLog(string.Format("DateTime {0}, System entered into Suspend Mode.", DateTime.Now.ToString()), LoggingMethod.LevelInfo);
				
				if (overrideTcpKeepAlive)
				{
					LoggingMethod.WriteToLog(string.Format("DateTime {0}, Disabled Timer for heartbeat ", DateTime.Now.ToString()), LoggingMethod.LevelInfo);
				}
			}
			else if (e.Mode == PowerModes.Resume)
			{
				_pubnetSystemActive = true;
				if (overrideTcpKeepAlive)
				{
					heartBeatTimer.Change(
						(-1 == _pubnubNetworkTcpCheckIntervalInSeconds) ? -1 : _pubnubNetworkTcpCheckIntervalInSeconds * 1000,
						(-1 == _pubnubNetworkTcpCheckIntervalInSeconds) ? -1 : _pubnubNetworkTcpCheckIntervalInSeconds * 1000);
				}
				
				LoggingMethod.WriteToLog(string.Format("DateTime {0}, System entered into Resume/Awake Mode.", DateTime.Now.ToString()), LoggingMethod.LevelInfo);
				
				if (overrideTcpKeepAlive)
				{
					LoggingMethod.WriteToLog(string.Format("DateTime {0}, Enabled Timer for heartbeat ", DateTime.Now.ToString()), LoggingMethod.LevelInfo);
				}
			}
		}
#endif
		private void TerminatePendingWebRequest()
		{
			TerminatePendingWebRequest<object>(null);
		}
		
		private void TerminatePendingWebRequest<T>(RequestState<T> state)
		{
			if (state != null && state.Request != null)
			{
                state.Request.Abort();
				LoggingMethod.WriteToLog(string.Format("DateTime {0} TerminatePendingWebRequest {1}", DateTime.Now.ToString(), state.Request.RequestUri.ToString()), LoggingMethod.LevelInfo);
			}
			else
			{
				ICollection<string> keyCollection = _channelRequest.Keys;
				foreach (string key in keyCollection)
				{
					PubnubWebRequest currentRequest = _channelRequest[key];
					if (currentRequest != null)
					{
                        currentRequest.Abort();
						LoggingMethod.WriteToLog(string.Format("DateTime {0} TerminatePendingWebRequest {1}", DateTime.Now.ToString(), currentRequest.RequestUri.ToString()), LoggingMethod.LevelInfo);
					}
				}
			}
		}
		
		private void RemoveChannelDictionary()
		{
			RemoveChannelDictionary<object>(null);
		}
		
		private void RemoveChannelDictionary<T>(RequestState<T> state)
		{
			if (state != null && state.Request != null)
			{
				string channel = (state.Channels != null) ? string.Join(",", state.Channels) : "";
				
				if (_channelRequest.ContainsKey(channel))
				{
					PubnubWebRequest removedRequest;
					bool removeKey = _channelRequest.TryRemove(channel, out removedRequest);
					if (removeKey)
					{
						LoggingMethod.WriteToLog(string.Format("DateTime {0} Remove web request from dictionary in RemoveChannelDictionary for channel= {1}", DateTime.Now.ToString(), channel), LoggingMethod.LevelInfo);
					}
					else
					{
						LoggingMethod.WriteToLog(string.Format("DateTime {0} Unable to remove web request from dictionary in TerminatePendingWebRequest for channel= {1}", DateTime.Now.ToString(), channel), LoggingMethod.LevelError);
					}
				}
			}
			else
			{
				ICollection<string> keyCollection = _channelRequest.Keys;
				foreach (string key in keyCollection)
				{
					PubnubWebRequest currentRequest = _channelRequest[key];
					if (currentRequest != null)
					{
						bool removeKey = _channelRequest.TryRemove(key, out currentRequest);
						if (removeKey)
						{
							LoggingMethod.WriteToLog(string.Format("DateTime {0} Remove web request from dictionary in RemoveChannelDictionary for channel= {1}", DateTime.Now.ToString(), key), LoggingMethod.LevelInfo);
						}
						else
						{
							LoggingMethod.WriteToLog(string.Format("DateTime {0} Unable to remove web request from dictionary in TerminatePendingWebRequest for channel= {1}", DateTime.Now.ToString(), key), LoggingMethod.LevelError);
						}
					}
				}
			}
		}

        private void RemoveChannelCallback()
        {
            ICollection<string> channelCollection = _channelCallbacks.Keys;
            foreach(string keyChannel in channelCollection)
            {
                if (_channelCallbacks.ContainsKey(keyChannel))
                {
                    object tempChannelCallback;
                    _channelCallbacks.TryRemove(keyChannel, out tempChannelCallback);
                }
            }
        }

#if (!SILVERLIGHT && !WINDOWS_PHONE && !MONOTOUCH && !__IOS__ && !MONODROID && !__ANDROID__ && !UNITY_STANDALONE && !UNITY_WEBPLAYER && !UNITY_IOS && !UNITY_ANDROID)
        ~Pubnub()
		{
			//detach
			SystemEvents.PowerModeChanged -= new PowerModeChangedEventHandler(SystemEvents_PowerModeChanged);
		}
#endif
		/**
         * PubNub 3.0 API
         * 
         * Prepare Pubnub messaging class initial state
         * 
         * @param string publishKey.
         * @param string subscribeKey.
         * @param string secretKey.
         * @param bool sslOn
         */
		public Pubnub(string publishKey, string subscribeKey, string secretKey, string cipherKey, bool sslOn)
		{
			this.Init(publishKey, subscribeKey, secretKey, cipherKey, sslOn);
		}
		
		/**
         * PubNub 2.0 Compatibility
         * 
         * Prepare Pubnub messaging class initial state
         * 
         * @param string publishKey.
         * @param string subscribeKey.
         */
		public Pubnub(string publishKey, string subscribeKey)
		{
			this.Init(publishKey, subscribeKey, "", "", false);
		}
		
		/// <summary>
		/// PubNub without SSL
		/// Prepare Pubnub messaging class initial state
		/// </summary>
		/// <param name="publishKey"></param>
		/// <param name="subscribeKey"></param>
		/// <param name="secretKey"></param>
		public Pubnub(string publishKey, string subscribeKey, string secretKey)
		{
			this.Init(publishKey, subscribeKey, secretKey, "", false);
		}
		
		/// <summary>
		/// History (Obsolete)
		/// Load history from a channel
		/// </summary>
		/// <param name="channel"></param>
		/// <param name="limit"></param>
		/// <returns></returns>
		[Obsolete("This method should no longer be used, please use DetailedHistory() instead.")]
		public bool history(string channel, int limit)
		{
			List<string> url = new List<string>();
			
			url.Add("history");
			url.Add(this.subscribeKey);
			url.Add(channel);
			url.Add("0");
			url.Add(limit.ToString());
			
			return ProcessRequest(url, ResponseType.History);
		}
		
		/**
         * Detailed History
         */
		public bool DetailedHistory(string channel, long start, long end, int count, bool reverse, Action<object> userCallback, Action<object> errorCallback)
		{
			return DetailedHistory<object>(channel, start, end, count, reverse, userCallback, errorCallback);
		}
		
		public bool DetailedHistory<T>(string channel, long start, long end, int count, bool reverse, Action<T> userCallback, Action<PubnubClientError> errorCallback)
		{
			if (string.IsNullOrEmpty(channel) || string.IsNullOrEmpty(channel.Trim()))
			{
				throw new ArgumentException("Missing Channel");
			}
			if (userCallback == null)
			{
				throw new ArgumentException("Missing userCallback");
			}
			if (errorCallback == null)
			{
				throw new ArgumentException("Missing errorCallback");
			}
			if (_jsonPluggableLibrary == null)
			{
				throw new NullReferenceException("Missing Json Pluggable Library for Pubnub Instance");
			}
			
			
			Uri request = BuildDetailedHistoryRequest(channel, start, end, count, reverse);
			
			RequestState<T> requestState = new RequestState<T>();
			requestState.Channels = new string[] { channel };
			requestState.Type = ResponseType.DetailedHistory;
			requestState.UserCallback = userCallback;
			requestState.ErrorCallback = errorCallback;
			requestState.Reconnect = false;
			
			return UrlProcessRequest<T>(request, requestState);
		}
		
		public bool DetailedHistory(string channel, long start, Action<object> userCallback, Action<object> errorCallback, bool reverse)
		{
			return DetailedHistory<object>(channel, start, -1, -1, reverse, userCallback, errorCallback);
		}
		
		public bool DetailedHistory<T>(string channel, long start, Action<T> userCallback, Action<PubnubClientError> errorCallback, bool reverse)
		{
			return DetailedHistory<T>(channel, start, -1, -1, reverse, userCallback, errorCallback);
		}
		
		public bool DetailedHistory(string channel, int count, Action<object> userCallback, Action<object> errorCallback)
		{
			return DetailedHistory<object>(channel, -1, -1, count, false, userCallback, errorCallback);
		}
		
		public bool DetailedHistory<T>(string channel, int count, Action<T> userCallback, Action<PubnubClientError> errorCallback)
		{
			return DetailedHistory<T>(channel, -1, -1, count, false, userCallback, errorCallback);
		}
		
		/// <summary>
		/// Publish
		/// Send a message to a channel
		/// </summary>
		/// <param name="channel"></param>
		/// <param name="message"></param>
		/// <param name="userCallback"></param>
		/// <returns></returns>
		public bool Publish(string channel, object message, Action<object> userCallback, Action<object> errorCallback)
		{
			return Publish<object>(channel, message, userCallback, errorCallback);
		}
		
		public bool Publish<T>(string channel, object message, Action<T> userCallback, Action<PubnubClientError> errorCallback)
		{
			if (string.IsNullOrEmpty(channel) || string.IsNullOrEmpty(channel.Trim()) || message == null)
			{
				throw new ArgumentException("Missing Channel or Message");
			}
			
			if (string.IsNullOrEmpty(this.publishKey) || string.IsNullOrEmpty(this.publishKey.Trim()) || this.publishKey.Length <= 0)
			{
				throw new MissingFieldException("Invalid publish key");
			}
			if (userCallback == null)
			{
				throw new ArgumentException("Missing userCallback");
			}
			if (errorCallback == null)
			{
				throw new ArgumentException("Missing errorCallback");
			}
			if (_jsonPluggableLibrary == null)
			{
				throw new NullReferenceException("Missing Json Pluggable Library for Pubnub Instance");
			}
			
			Uri request = BuildPublishRequest(channel, message);
			
			RequestState<T> requestState = new RequestState<T>();
			requestState.Channels = new string[] { channel };
			requestState.Type = ResponseType.Publish;
			requestState.UserCallback = userCallback;
			requestState.ErrorCallback = errorCallback;
			requestState.Reconnect = false;
			
			return UrlProcessRequest<T>(request, requestState); 
		}
		
		private string JsonEncodePublishMsg(object originalMessage)
		{
			string message = _jsonPluggableLibrary.SerializeToJsonString(originalMessage);
			
			
			if (this.cipherKey.Length > 0)
			{
				PubnubCrypto aes = new PubnubCrypto(this.cipherKey);
				string encryptMessage = aes.Encrypt(message);
				message = _jsonPluggableLibrary.SerializeToJsonString(encryptMessage);
			}
			
			return message;
		}
		
		private List<object> DecodeDecryptLoop(List<object> message, string[] channels, Action<PubnubClientError> errorCallback)
		{
			List<object> returnMessage = new List<object>();
			if (this.cipherKey.Length > 0)
			{
				PubnubCrypto aes = new PubnubCrypto(this.cipherKey);
				var myObjectArray = (from item in message select item as object).ToArray();
				IEnumerable enumerable = myObjectArray[0] as IEnumerable;
				if (enumerable != null)
				{
					List<object> receivedMsg = new List<object>();
					foreach (object element in enumerable)
					{
						string decryptMessage = "";
                        try
                        {
                            decryptMessage = aes.Decrypt(element.ToString());
                        }
                        catch (Exception ex)
                        {
                            decryptMessage = "**DECRYPT ERROR**";
                            //TODO: Identify refactoring
                            string multiChannel = string.Join(",", channels);

                            PubnubErrorCode errorType = PubnubErrorCodeHelper.GetErrorType(ex);
                            int statusCode = (int)errorType;
                            string errorDescription = PubnubErrorCodeDescription.GetStatusCodeDescription(errorType);
                            PubnubClientError error = new PubnubClientError(statusCode, PubnubErrorSeverity.Warn, true, ex.Message, ex, PubnubMessageSource.Client, null, null, errorDescription, multiChannel);
                            GoToCallback(error, errorCallback);
                        }
						object decodeMessage = (decryptMessage == "**DECRYPT ERROR**") ? decryptMessage : _jsonPluggableLibrary.DeserializeToObject(decryptMessage);
						receivedMsg.Add(decodeMessage);
					}
					returnMessage.Add(receivedMsg);
				}
				
				for (int index = 1; index < myObjectArray.Length; index++)
				{
					returnMessage.Add(myObjectArray[index]);
				}
				return returnMessage;
			}
			else
			{
				var myObjectArray = (from item in message select item as object).ToArray();
				IEnumerable enumerable = myObjectArray[0] as IEnumerable;
				if (enumerable != null)
				{
					List<object> receivedMessage = new List<object>();
					foreach (object element in enumerable)
					{
						receivedMessage.Add(element);
					}
					returnMessage.Add(receivedMessage);
				}
				for (int index = 1; index < myObjectArray.Length; index++)
				{
					returnMessage.Add(myObjectArray[index]);
				}
				return returnMessage;
			}
		}
		
		
		/// <summary>
		/// Subscribe
		/// Listen for a message on a channel
		/// </summary>
		/// <param name="channel"></param>
		/// <param name="userCallback"></param>
		/// <param name="connectCallback"></param>
		public void Subscribe(string channel, Action<object> userCallback, Action<object> connectCallback, Action<object> errorCallback)
		{
			Subscribe<object>(channel, userCallback, connectCallback, errorCallback);
		}
		
		public void Subscribe<T>(string channel, Action<T> userCallback, Action<T> connectCallback, Action<PubnubClientError> errorCallback)
		{
			if (string.IsNullOrEmpty(channel) || string.IsNullOrEmpty(channel.Trim()))
			{
				throw new ArgumentException("Missing Channel");
			}
			if (userCallback == null)
			{
				throw new ArgumentException("Missing userCallback");
			}
			if (connectCallback == null)
			{
				throw new ArgumentException("Missing connectCallback");
			}
			if (errorCallback == null)
			{
				throw new ArgumentException("Missing errorCallback");
			}
			if (_jsonPluggableLibrary == null)
			{
				throw new NullReferenceException("Missing Json Pluggable Library for Pubnub Instance");
			}
			
			LoggingMethod.WriteToLog(string.Format("DateTime {0}, requested subscribe for channel={1}", DateTime.Now.ToString(), channel), LoggingMethod.LevelInfo);
			
			MultiChannelSubscribeInit<T>(ResponseType.Subscribe, channel, userCallback, connectCallback, errorCallback);
		}
		
		private void MultiChannelSubscribeInit<T>(ResponseType type, string channel, Action<T> userCallback, Action<T> connectCallback, Action<PubnubClientError> errorCallback)
		{
			string[] rawChannels = channel.Split(',');
			List<string> validChannels = new List<string>();
            
            bool networkConnection;
            if (_pubnubUnitTest is IPubnubUnitTest && _pubnubUnitTest.EnableStubTest)
            {
                networkConnection = true;
            }
            else
            {
                networkConnection = ClientNetworkStatus.CheckInternetStatus<T>(_pubnetSystemActive, errorCallback, rawChannels);
                if (!networkConnection)
                {
                    //List<object> result = new List<object>();
                    string message = "Network connnect error";
                    //result = _jsonPluggableLibrary.DeserializeToListOfObject(jsonString);
                    //result.Add(channel);
                    PubnubErrorCode errorType = PubnubErrorCode.NoInternet;
                    int statusCode = (int)errorType;
                    string errorDescription = PubnubErrorCodeDescription.GetStatusCodeDescription(errorType);
                    PubnubClientError error = new PubnubClientError(statusCode, PubnubErrorSeverity.Info, message, PubnubMessageSource.Client, null, null, errorDescription, channel);
                    GoToCallback(error, errorCallback);
                }
            }

			if (rawChannels.Length > 0 && networkConnection)
			{
				if (rawChannels.Length != rawChannels.Distinct().Count())
				{
					rawChannels = rawChannels.Distinct().ToArray();
					//List<object> result = new List<object>();
					string message = "Detected and removed duplicate channels";
					//result = _jsonPluggableLibrary.DeserializeToListOfObject(jsonString);
					//result.Add(channel);
                    PubnubErrorCode errorType = PubnubErrorCode.DuplicateChannel;
                    int statusCode = (int)errorType;
                    string errorDescription = PubnubErrorCodeDescription.GetStatusCodeDescription(errorType);
                    PubnubClientError error = new PubnubClientError(statusCode, PubnubErrorSeverity.Info, message, PubnubMessageSource.Client, null, null, errorDescription, channel);
                    GoToCallback(error, errorCallback);
				}
				
				for (int index = 0; index < rawChannels.Length; index++)
				{
					if (rawChannels[index].Trim().Length > 0)
					{
						string channelName = rawChannels[index].Trim();
						if (type == ResponseType.Presence)
						{
							channelName = string.Format("{0}-pnpres", channelName);
						}
						if (_multiChannelSubscribe.ContainsKey(channelName))
						{
							//List<object> result = new List<object>();
                            string message = string.Format("{0}Already subscribed", (IsPresenceChannel(channelName)) ? "Presence " : "");
							
							//result = _jsonPluggableLibrary.DeserializeToListOfObject(jsonString);
							//result.Add(channelName.Replace("-pnpres",""));
                            PubnubErrorCode errorType = (IsPresenceChannel(channelName)) ? PubnubErrorCode.AlreadyPresenceSubscribed : PubnubErrorCode.AlreadySubscribed;
                            int statusCode = (int)errorType;
                            string errorDescription = PubnubErrorCodeDescription.GetStatusCodeDescription(errorType);
                            LoggingMethod.WriteToLog(string.Format("DateTime {0}, channel={1} subscribe response={2}", DateTime.Now.ToString(), channelName, message), LoggingMethod.LevelInfo);
                            PubnubClientError error = new PubnubClientError(statusCode, PubnubErrorSeverity.Info, message, PubnubMessageSource.Client, null, null, errorDescription, channelName.Replace("-pnpres", ""));
                            GoToCallback(error, errorCallback);
						}
						else
						{
							validChannels.Add(channelName);
						}
					}
                    //else
                    //{
                    //    //List<object> result = new List<object>();
                    //    string message = "Invalid Channel Name";
                    //    //result = _jsonPluggableLibrary.DeserializeToListOfObject(message);
                    //    //result.Add(channel.Replace("-pnpres", ""));
                    //    LoggingMethod.WriteToLog(string.Format("DateTime {0}, JSON response={1}", DateTime.Now.ToString(), message), LoggingMethod.LevelInfo);
                    //    PubnubClientError error = new PubnubClientError(PubnubErrorCategory.Informational, message, PubnubMessageSource.Client, channel.Replace("-pnpres", ""));
                    //    GoToCallback(error, errorCallback);
                    //}
				}
			}
			
			if (validChannels.Count > 0)
			{
				//Retrieve the current channels already subscribed previously and terminate them
				string[] currentChannels = _multiChannelSubscribe.Keys.ToArray<string>();
				if (currentChannels != null && currentChannels.Length > 0)
				{
					string multiChannelName = string.Join(",", currentChannels);
					if (_channelRequest.ContainsKey(multiChannelName))
					{
						LoggingMethod.WriteToLog(string.Format("DateTime {0}, Aborting previous subscribe/presence requests having channel(s)={1}", DateTime.Now.ToString(), multiChannelName), LoggingMethod.LevelInfo);
						PubnubWebRequest webRequest = _channelRequest[multiChannelName];
                        _channelRequest[multiChannelName] = null;
						
						TerminateHeartbeatTimer(webRequest.RequestUri);
						
						PubnubWebRequest removedRequest;
						_channelRequest.TryRemove(multiChannelName, out removedRequest);
                        bool removedChannel = _channelRequest.TryRemove(multiChannelName, out removedRequest);
                        if (removedChannel)
                        {
                            LoggingMethod.WriteToLog(string.Format("DateTime {0}, Success to remove channel(s)={1} from _channelRequest (MultiChannelSubscribeInit).", DateTime.Now.ToString(), multiChannelName), LoggingMethod.LevelInfo);
                        }
                        else
                        {
                            LoggingMethod.WriteToLog(string.Format("DateTime {0}, Unable to remove channel(s)={1} from _channelRequest (MultiChannelSubscribeInit).", DateTime.Now.ToString(), multiChannelName), LoggingMethod.LevelInfo);
                        }
						webRequest.Abort();
					}
					else
					{
						LoggingMethod.WriteToLog(string.Format("DateTime {0}, Unable to capture channel(s)={1} from _channelRequest to abort request.", DateTime.Now.ToString(), multiChannelName), LoggingMethod.LevelInfo);
					}
				}
				
				
				//Add the valid channels to the channels subscribe list for tracking
				for (int index = 0; index < validChannels.Count; index++)
				{
                    string currentLoopChannel = validChannels[index].ToString();
                    _multiChannelSubscribe.GetOrAdd(currentLoopChannel, 0);
                    
                    PubnubChannelCallback<T> channelCallbacks = new PubnubChannelCallback<T>();
                    //channelCallbacks.Type = type;
                    channelCallbacks.Callback = userCallback;
                    channelCallbacks.ConnectCallback = connectCallback;
                    channelCallbacks.ErrorCallback = errorCallback;

                    _channelCallbacks.AddOrUpdate(currentLoopChannel, channelCallbacks, (key, oldValue) => channelCallbacks);
                }
				
				//Get all the channels
				string[] channels = _multiChannelSubscribe.Keys.ToArray<string>();
				
				RequestState<T> state = new RequestState<T>();
				_channelRequest.AddOrUpdate(string.Join(",", channels), state.Request, (key, oldValue) => state.Request);
				
				ResetInternetCheckSettings(channels);
				MultiChannelSubscribeRequest<T>(type, channels, 0, userCallback, connectCallback, errorCallback, false);
			}
			
		}
		
		private void MultiChannelUnSubscribeInit<T>(ResponseType type, string channel, Action<T> userCallback, Action<T> connectCallback, Action<T> disconnectCallback, Action<PubnubClientError> errorCallback)
		{
			string[] rawChannels = channel.Split(',');
			List<string> validChannels = new List<string>();
			
			if (rawChannels.Length > 0)
			{
				for (int index = 0; index < rawChannels.Length; index++)
				{
					if (rawChannels[index].Trim().Length > 0)
					{
						string channelName = rawChannels[index].Trim();
						if (type == ResponseType.PresenceUnsubscribe)
						{
							channelName = string.Format("{0}-pnpres", channelName);
						}
						if (!_multiChannelSubscribe.ContainsKey(channelName))
						{
                            string message = string.Format("{0}Channel Not Subscribed", (IsPresenceChannel(channelName)) ? "Presence " : "");
                            
                            LoggingMethod.WriteToLog(string.Format("DateTime {0}, channel={1} unsubscribe response={2}", DateTime.Now.ToString(), channelName, message), LoggingMethod.LevelInfo);
                            
                            PubnubClientError error = new PubnubClientError(0, PubnubErrorSeverity.Info, message, PubnubMessageSource.Client, channelName);
                            GoToCallback(error, errorCallback);
						}
						else
						{
							validChannels.Add(channelName);
						}
					}
					else
					{
						string message = "Invalid Channel Name For Unsubscribe";

                        LoggingMethod.WriteToLog(string.Format("DateTime {0}, channel={1} unsubscribe response={2}", DateTime.Now.ToString(), rawChannels[index], message), LoggingMethod.LevelInfo);

                        PubnubClientError error = new PubnubClientError(0, PubnubErrorSeverity.Info, message, PubnubMessageSource.Client, rawChannels[index]);
                        GoToCallback(error, errorCallback);
					}
				}
			}
			
			if (validChannels.Count > 0)
			{
				//Retrieve the current channels already subscribed previously and terminate them
				string[] currentChannels = _multiChannelSubscribe.Keys.ToArray<string>();
				if (currentChannels != null && currentChannels.Length > 0)
				{
					string multiChannelName = string.Join(",", currentChannels);
					if (_channelRequest.ContainsKey(multiChannelName))
					{
						LoggingMethod.WriteToLog(string.Format("DateTime {0}, Aborting previous subscribe/presence requests having channel(s)={1}", DateTime.Now.ToString(), multiChannelName), LoggingMethod.LevelInfo);
						PubnubWebRequest webRequest = _channelRequest[multiChannelName];
                        _channelRequest[multiChannelName] = null;
						
						TerminateHeartbeatTimer(webRequest.RequestUri);
						
						PubnubWebRequest removedRequest;
                        bool removedChannel = _channelRequest.TryRemove(multiChannelName, out removedRequest);
                        if (removedChannel)
                        {
                            LoggingMethod.WriteToLog(string.Format("DateTime {0}, Success to remove channel(s)={1} from _channelRequest (MultiChannelUnSubscribeInit).", DateTime.Now.ToString(), multiChannelName), LoggingMethod.LevelInfo);
                        }
                        else
                        {
                            LoggingMethod.WriteToLog(string.Format("DateTime {0}, Unable to remove channel(s)={1} from _channelRequest (MultiChannelUnSubscribeInit).", DateTime.Now.ToString(), multiChannelName), LoggingMethod.LevelInfo);
                        }
						webRequest.Abort();
					}
					else
					{
						LoggingMethod.WriteToLog(string.Format("DateTime {0}, Unable to capture channel(s)={1} from _channelRequest to abort request.", DateTime.Now.ToString(), multiChannelName), LoggingMethod.LevelInfo);
					}

                    if (type == ResponseType.Unsubscribe)
                    {
                        //just fire leave() event to REST API for safeguard
                        Uri request = BuildMultiChannelLeaveRequest(validChannels.ToArray());

                        RequestState<T> requestState = new RequestState<T>();
                        requestState.Channels = new string[] { channel };
                        requestState.Type = ResponseType.Leave;
                        requestState.UserCallback = null;
                        requestState.ErrorCallback = null;
                        requestState.ConnectCallback = null;
                        requestState.Reconnect = false;

                        UrlProcessRequest<T>(request, requestState); // connectCallback = null
                    }
                }
				
				
				//Remove the valid channels from subscribe list for unsubscribe 
				for (int index = 0; index < validChannels.Count; index++)
				{
					long timetokenValue;
					string channelToBeRemoved = validChannels[index].ToString();
					bool unsubscribeStatus = _multiChannelSubscribe.TryRemove(channelToBeRemoved, out timetokenValue);
					if (unsubscribeStatus)
					{
						List<object> result = new List<object>();
						string jsonString = string.Format("[1, \"{0}Unsubscribed from {1}\"]", (IsPresenceChannel(channelToBeRemoved)) ? "Presence " : "",  channelToBeRemoved.Replace("-pnpres", ""));
						result = _jsonPluggableLibrary.DeserializeToListOfObject(jsonString);
						result.Add(channelToBeRemoved.Replace("-pnpres", ""));
						LoggingMethod.WriteToLog(string.Format("DateTime {0}, JSON response={1}", DateTime.Now.ToString(), jsonString), LoggingMethod.LevelInfo);
						GoToCallback<T>(result, disconnectCallback);
					}
					else
					{
                        string message = "Unsubscribe Error";
                        LoggingMethod.WriteToLog(string.Format("DateTime {0}, channel={1} unsubscrib error", DateTime.Now.ToString(), channelToBeRemoved), LoggingMethod.LevelInfo);
                        PubnubClientError error = new PubnubClientError(0, PubnubErrorSeverity.Info, message, PubnubMessageSource.Client, channelToBeRemoved);
                        GoToCallback(error, errorCallback);
                    }
				}
				
				//Get all the channels
				string[] channels = _multiChannelSubscribe.Keys.ToArray<string>();
				
				if (channels != null && channels.Length > 0)
				{
					RequestState<T> state = new RequestState<T>();
					_channelRequest.AddOrUpdate(string.Join(",", channels), state.Request, (key, oldValue) => state.Request);
					
					ResetInternetCheckSettings(channels);
					
					//Modify the value for type ResponseType. Presence or Subscrie is ok, but sending the close value would make sense
					if (string.Join(",", channels).IndexOf("-pnpres") > 0)
					{
						type = ResponseType.Presence;
					}
					else
					{
						type = ResponseType.Subscribe;
					}
					
					//Continue with any remaining channels for subscribe/presence
					MultiChannelSubscribeRequest<T>(type, channels, 0, userCallback, connectCallback, errorCallback, false);
				}
				else
				{
					LoggingMethod.WriteToLog(string.Format("DateTime {0}, All channels are Unsubscribed. Further subscription was stopped", DateTime.Now.ToString()), LoggingMethod.LevelInfo);
				}
			}
			
		}
		
		private void ResetInternetCheckSettings(string[] channels)
		{
			if (channels == null) return;
			
			string multiChannel = string.Join(",", channels);
			if (_channelInternetStatus.ContainsKey(multiChannel))
			{
				_channelInternetStatus.AddOrUpdate(multiChannel, true, (key, oldValue) => true);
			}
			else
			{
				_channelInternetStatus.GetOrAdd(multiChannel, true); //Set to true for internet connection
			}
			
			if (_channelInternetRetry.ContainsKey(multiChannel))
			{
				_channelInternetRetry.AddOrUpdate(multiChannel, 0, (key, oldValue) => 0);
			}
			else
			{
				_channelInternetRetry.GetOrAdd(multiChannel, 0); //Initialize the internet retry count
			}
		}
		
		void OnPubnubWebRequestTimeout<T>(object state, bool timeout)
		{
			if (timeout)
			{
				RequestState<T> currentState = state as RequestState<T>;
				if (currentState != null)
				{
					PubnubWebRequest request = currentState.Request;
					if (request != null)
					{
                        string currentMultiChannel = (currentState.Channels == null) ? "" : string.Join(",", currentState.Channels);
                        LoggingMethod.WriteToLog(string.Format("DateTime: {0}, OnPubnubWebRequestTimeout: client request timeout reached.Request abort for channel = {1}", DateTime.Now.ToString(), currentMultiChannel), LoggingMethod.LevelInfo);
						currentState.Timeout = true;
						TerminatePendingWebRequest(currentState);
					}
				}
				else
				{
					LoggingMethod.WriteToLog(string.Format("DateTime: {0}, OnPubnubWebRequestTimeout: client request timeout reached. However state is unknown", DateTime.Now.ToString()), LoggingMethod.LevelError);
				}
			}
		}
		
		void OnPubnubHeartBeatTimeoutCallback<T>(System.Object heartbeatState)
		{
			LoggingMethod.WriteToLog(string.Format("DateTime: {0}, **OnPubnubHeartBeatTimeoutCallback**", DateTime.Now.ToString()), LoggingMethod.LevelVerbose);
			
			RequestState<T> currentState = heartbeatState as RequestState<T>;
			if (currentState != null)
			{
				string channel = (currentState.Channels != null) ? string.Join(",", currentState.Channels) : "";
				
				if (_channelInternetStatus.ContainsKey(channel)
				    && (currentState.Type == ResponseType.Subscribe || currentState.Type == ResponseType.Presence)
				    && overrideTcpKeepAlive)
				{
					bool networkConnection;
					if (_pubnubUnitTest is IPubnubUnitTest && _pubnubUnitTest.EnableStubTest)
					{
						networkConnection = true;
					}
					else
					{
						networkConnection = ClientNetworkStatus.CheckInternetStatus<T>(_pubnetSystemActive, currentState.ErrorCallback, currentState.Channels);
					}
					
					_channelInternetStatus[channel] = networkConnection;
					
					LoggingMethod.WriteToLog(string.Format("DateTime: {0}, OnPubnubHeartBeatTimeoutCallback - Internet connection = {1}", DateTime.Now.ToString(), networkConnection), LoggingMethod.LevelVerbose);
					if (!networkConnection)
					{
						TerminatePendingWebRequest(currentState);
					}
				}
			}
		}
		
		
		/// <summary>
		/// Check the response of the REST API and call for re-subscribe
		/// </summary>
		/// <typeparam name="T"></typeparam>
		/// <param name="type"></param>
		/// <param name="multiplexResult"></param>
		/// <param name="userCallback"></param>
		/// <param name="connectCallback"></param>
		/// <param name="errorCallback"></param>
		private void MultiplexInternalCallback<T>(ResponseType type, object multiplexResult, Action<T> userCallback, Action<T> connectCallback, Action<PubnubClientError> errorCallback)
		{
			List<object> message = multiplexResult as List<object>;
			string[] channels = null;
			if (message != null && message.Count >= 3)
			{
				if (message[message.Count - 1] is string[])
				{
					channels = message[message.Count - 1] as string[];
				}
				else
				{
					channels = message[message.Count - 1].ToString().Split(',') as string[];
				}
			}
			else
			{
				LoggingMethod.WriteToLog(string.Format("DateTime {0}, Lost Channel Name for resubscribe", DateTime.Now.ToString()), LoggingMethod.LevelError);
				return;
			}
			
			if (message != null && message.Count >= 3)
			{
				MultiChannelSubscribeRequest<T>(type, channels, (object)message[1], userCallback, connectCallback, errorCallback, false);
			}
		}
		
		/// <summary>
		/// To unsubscribe a channel
		/// </summary>
		/// <param name="channel"></param>
		/// <param name="userCallback"></param>
		/// <param name="connectCallback"></param>
		/// <param name="disconnectCallback"></param>
		/// <param name="errorCallback"></param>
		public void Unsubscribe(string channel, Action<object> userCallback, Action<object> connectCallback, Action<object> disconnectCallback, Action<object> errorCallback)
		{
			Unsubscribe<object>(channel, userCallback, connectCallback, disconnectCallback, errorCallback);
		}
		
		/// <summary>
		/// To unsubscribe a channel
		/// </summary>
		/// <typeparam name="T"></typeparam>
		/// <param name="channel"></param>
		/// <param name="userCallback"></param>
		/// <param name="connectCallback"></param>
		/// <param name="disconnectCallback"></param>
		/// <param name="errorCallback"></param>
		public void Unsubscribe<T>(string channel, Action<T> userCallback, Action<T> connectCallback, Action<T> disconnectCallback, Action<PubnubClientError> errorCallback)
		{
			if (string.IsNullOrEmpty(channel) || string.IsNullOrEmpty(channel.Trim()))
			{
				throw new ArgumentException("Missing Channel");
			}
			if (userCallback == null)
			{
				throw new ArgumentException("Missing userCallback");
			}
			if (connectCallback == null)
			{
				throw new ArgumentException("Missing connectCallback");
			}
			if (disconnectCallback == null)
			{
				throw new ArgumentException("Missing disconnectCallback");
			}
			if (errorCallback == null)
			{
				throw new ArgumentException("Missing errorCallback");
			}
			if (_jsonPluggableLibrary == null)
			{
				throw new NullReferenceException("Missing Json Pluggable Library for Pubnub Instance");
			}
			
			LoggingMethod.WriteToLog(string.Format("DateTime {0}, requested unsubscribe for channel(s)={1}", DateTime.Now.ToString(), channel), LoggingMethod.LevelInfo);
			MultiChannelUnSubscribeInit<T>(ResponseType.Unsubscribe, channel, userCallback, connectCallback, disconnectCallback, errorCallback);
			
		}
		
		/// <summary>
		/// Multi-Channel Subscribe Request - private method for Subscribe
		/// </summary>
		/// <typeparam name="T"></typeparam>
		/// <param name="type"></param>
		/// <param name="channels"></param>
		/// <param name="timetoken"></param>
		/// <param name="userCallback"></param>
		/// <param name="connectCallback"></param>
		/// <param name="errorCallback"></param>
		/// <param name="reconnect"></param>
		private void MultiChannelSubscribeRequest<T>(ResponseType type, string[] channels, object timetoken, Action<T> userCallback, Action<T> connectCallback, Action<PubnubClientError> errorCallback, bool reconnect)
		{
			//Exit if the channel is unsubscribed
			if (_multiChannelSubscribe != null && _multiChannelSubscribe.Count <= 0)
			{
				LoggingMethod.WriteToLog(string.Format("DateTime {0}, All channels are Unsubscribed. Further subscription was stopped", DateTime.Now.ToString()), LoggingMethod.LevelInfo);
				return;
			}
			
			string multiChannel = string.Join(",", channels);
			if (!_channelRequest.ContainsKey(multiChannel))
			{
				return;
			}
			
			if (_channelInternetStatus.ContainsKey(multiChannel) && (!_channelInternetStatus[multiChannel]) && _pubnetSystemActive)
			{
				if (_channelInternetRetry.ContainsKey(multiChannel) && (_channelInternetRetry[multiChannel] >= _pubnubNetworkCheckRetries))
				{
					LoggingMethod.WriteToLog(string.Format("DateTime {0}, Subscribe channel={1} - No internet connection. MAXed retries for internet ", DateTime.Now.ToString(), multiChannel), LoggingMethod.LevelInfo);
					MultiplexExceptionHandler<T>(type,channels, userCallback, connectCallback, errorCallback, true, false);
					return;
				}
				
				if (overrideTcpKeepAlive)
				{
					LoggingMethod.WriteToLog(string.Format("DateTime {0}, Subscribe - No internet connection for {1}", DateTime.Now.ToString(), multiChannel), LoggingMethod.LevelInfo);
					
					ReconnectState<T> netState = new ReconnectState<T>();
					netState.Channels = channels;
					netState.Type = type;
					netState.Callback = userCallback;
					netState.ErrorCallback = errorCallback;
					netState.ConnectCallback = connectCallback;
					netState.Timetoken = timetoken;
					
					ReconnectNetwork<T>(netState);
					return;
				}
			}
			
			// Begin recursive subscribe
			try
			{
				long lastTimetoken = 0;
				long minimumTimetoken = _multiChannelSubscribe.Min(token => token.Value);
				long maximumTimetoken = _multiChannelSubscribe.Max(token => token.Value);
				
				if (minimumTimetoken == 0 || reconnect)
				{
					lastTimetoken = 0;
				}
				else
				{
					if (lastSubscribeTimetoken == maximumTimetoken)
					{
						lastTimetoken = maximumTimetoken;
					}
					else
					{
						lastTimetoken = lastSubscribeTimetoken;
					}
				}
				LoggingMethod.WriteToLog(string.Format("DateTime {0}, Building request for channel(s)={1} with timetoken={2}", DateTime.Now.ToString(), string.Join(",",channels),lastTimetoken), LoggingMethod.LevelInfo);
				// Build URL
				Uri requestUrl = BuildMultiChannelSubscribeRequest(channels, (Convert.ToInt64(timetoken.ToString()) == 0) ? Convert.ToInt64(timetoken.ToString()) : lastTimetoken);
				
				RequestState<T> pubnubRequestState = new RequestState<T>();
				pubnubRequestState.Channels = channels;
				pubnubRequestState.Type = type;
				pubnubRequestState.ConnectCallback = connectCallback;
				pubnubRequestState.UserCallback = userCallback;
				pubnubRequestState.ErrorCallback = errorCallback;
				pubnubRequestState.Reconnect = reconnect;
				pubnubRequestState.Timetoken = Convert.ToInt64(timetoken.ToString());
				
				// Wait for message
				UrlProcessRequest<T>(requestUrl, pubnubRequestState);
			}
			catch (Exception ex)
			{
                LoggingMethod.WriteToLog(string.Format("DateTime {0} method:_subscribe \n channel={1} \n timetoken={2} \n Exception Details={3}", DateTime.Now.ToString(), string.Join(",", channels), timetoken.ToString(), ex.ToString()), LoggingMethod.LevelError);
                PubnubErrorCode errorType = PubnubErrorCodeHelper.GetErrorType(ex);
                int statusCode = (int)errorType;
                string errorDescription = PubnubErrorCodeDescription.GetStatusCodeDescription(errorType);
                PubnubClientError error = new PubnubClientError(statusCode, PubnubErrorSeverity.Warn, true, ex.Message, ex, PubnubMessageSource.Client, null, null, errorDescription, string.Join(",", channels));
                GoToCallback(error, errorCallback);
				
				this.MultiChannelSubscribeRequest<T>(type, channels, timetoken, userCallback, connectCallback, errorCallback, false);
			}
		}
		
		/// <summary>
		/// Presence
		/// Listen for a presence message on a channel or comma delimited channels
		/// </summary>
		/// <param name="channel"></param>
		/// <param name="userCallback"></param>
		/// <param name="connectCallback"></param>
		/// <param name="errorCallback"></param>
		public void Presence(string channel, Action<object> userCallback, Action<object> connectCallback, Action<object> errorCallback)
		{
			Presence<object>(channel, userCallback, connectCallback, errorCallback);
		}
		
		public void Presence<T>(string channel, Action<T> userCallback, Action<T> connectCallback, Action<PubnubClientError> errorCallback)
		{
			if (string.IsNullOrEmpty(channel) || string.IsNullOrEmpty(channel.Trim()))
			{
				throw new ArgumentException("Missing Channel");
			}
			if (userCallback == null)
			{
				throw new ArgumentException("Missing userCallback");
			}
			if (errorCallback == null)
			{
				throw new ArgumentException("Missing errorCallback");
			}
			if (_jsonPluggableLibrary == null)
			{
				throw new NullReferenceException("Missing Json Pluggable Library for Pubnub Instance");
			}
			
			LoggingMethod.WriteToLog(string.Format("DateTime {0}, requested presence for channel={1}", DateTime.Now.ToString(), channel), LoggingMethod.LevelInfo);
			
			MultiChannelSubscribeInit<T>(ResponseType.Presence, channel, userCallback, connectCallback, errorCallback);
		}
		
		public void PresenceUnsubscribe(string channel, Action<object> userCallback, Action<object> connectCallback, Action<object> disconnectCallback, Action<object> errorCallback)
		{
			PresenceUnsubscribe<object>(channel, userCallback, connectCallback, disconnectCallback, errorCallback);
		}
		
		public void PresenceUnsubscribe<T>(string channel, Action<T> userCallback, Action<T> connectCallback, Action<T> disconnectCallback, Action<PubnubClientError> errorCallback)
		{
			if (string.IsNullOrEmpty(channel) || string.IsNullOrEmpty(channel.Trim()))
			{
				throw new ArgumentException("Missing Channel");
			}
			if (userCallback == null)
			{
				throw new ArgumentException("Missing userCallback");
			}
			if (connectCallback == null)
			{
				throw new ArgumentException("Missing connectCallback");
			}
			if (disconnectCallback == null)
			{
				throw new ArgumentException("Missing disconnectCallback");
			}
			if (errorCallback == null)
			{
				throw new ArgumentException("Missing errorCallback");
			}
			if (_jsonPluggableLibrary == null)
			{
				throw new NullReferenceException("Missing Json Pluggable Library for Pubnub Instance");
			}
			
			LoggingMethod.WriteToLog(string.Format("DateTime {0}, requested presence-unsubscribe for channel(s)={1}", DateTime.Now.ToString(), channel), LoggingMethod.LevelInfo);
			MultiChannelUnSubscribeInit<T>(ResponseType.PresenceUnsubscribe, channel, userCallback, connectCallback, disconnectCallback, errorCallback);
		}
		
		public bool HereNow(string channel, Action<object> userCallback, Action<object> errorCallback)
		{
			return HereNow<object>(channel, userCallback, errorCallback);
		}
		
		public bool HereNow<T>(string channel, Action<T> userCallback, Action<PubnubClientError> errorCallback)
		{
			if (string.IsNullOrEmpty(channel) || string.IsNullOrEmpty(channel.Trim()))
			{
				throw new ArgumentException("Missing Channel");
			}
			if (userCallback == null)
			{
				throw new ArgumentException("Missing userCallback");
			}
			if (errorCallback == null)
			{
				throw new ArgumentException("Missing errorCallback");
			}
			if (_jsonPluggableLibrary == null)
			{
				throw new NullReferenceException("Missing Json Pluggable Library for Pubnub Instance");
			}
			
			
			Uri request = BuildHereNowRequest(channel);
			
			RequestState<T> requestState = new RequestState<T>();
			requestState.Channels = new string[] { channel };
			requestState.Type = ResponseType.Here_Now;
			requestState.UserCallback = userCallback;
			requestState.ErrorCallback = errorCallback;
			requestState.Reconnect = false;
			
			return UrlProcessRequest<T>(request, requestState); 
		}

		/// <summary>
		/// Time
		/// Timestamp from PubNub Cloud
		/// </summary>
		/// <param name="userCallback"></param>
		/// <param name="errorCallback"></param>
		/// <returns></returns>
		public bool Time(Action<object> userCallback, Action<PubnubClientError> errorCallback)
		{
			return Time<object>(userCallback, errorCallback);
		}
		
		public bool Time<T>(Action<T> userCallback, Action<PubnubClientError> errorCallback)
		{
			if (userCallback == null)
			{
				throw new ArgumentException("Missing userCallback");
			}
			if (errorCallback == null)
			{
				throw new ArgumentException("Missing errorCallback");
			}
			if (_jsonPluggableLibrary == null)
			{
				throw new NullReferenceException("Missing Json Pluggable Library for Pubnub Instance");
			}
			
			Uri request = BuildTimeRequest();
			
			RequestState<T> requestState = new RequestState<T>();
			requestState.Channels = null;
			requestState.Type = ResponseType.Time;
			requestState.UserCallback = userCallback;
			requestState.ErrorCallback = errorCallback;
			requestState.Reconnect = false;
			
			return UrlProcessRequest<T>(request, requestState); 
		}
		
		/// <summary>
		/// Http Get Request process
		/// </summary>
		/// <param name="urlComponents"></param>
		/// <param name="type"></param>
		/// <returns></returns>
		private bool ProcessRequest(List<string> urlComponents, ResponseType type)
		{
			string channelName = GetChannelName(urlComponents, type);
			StringBuilder url = new StringBuilder();
			
			// Add Origin To The Request
			url.Append(this._origin);
			
			// Generate URL with UTF-8 Encoding
			foreach (string url_bit in urlComponents)
			{
				url.Append("/");
				url.Append(EncodeUricomponent(url_bit, type, true));
			}
			
			VerifyOrSetSessionUUID();
			if (type == ResponseType.Presence || type == ResponseType.Subscribe)
			{
				url.Append("?uuid=");
				url.Append(this.sessionUUID);
			}
			
			if (type == ResponseType.DetailedHistory)
				url.Append(parameters);
			
			Uri requestUri = new Uri(url.ToString());
			
#if ((!__MonoCS__) && (!SILVERLIGHT) && !WINDOWS_PHONE && !UNITY_STANDALONE && !UNITY_WEBPLAYER && !UNITY_IOS && !UNITY_ANDROID)
			// Force canonical path and query
			FieldInfo flagsFieldInfo = typeof(Uri).GetField("m_Flags", BindingFlags.Instance | BindingFlags.NonPublic);
			ulong flags = (ulong)flagsFieldInfo.GetValue(requestUri);
			flags &= ~((ulong)0x30); // Flags.PathNotCanonical|Flags.QueryNotCanonical
			flagsFieldInfo.SetValue(requestUri, flags);
#endif
			
			// Create Request
			HttpWebRequest request = (HttpWebRequest)WebRequest.Create(requestUri);
			
			try
			{
				// Make request with the following inline Asynchronous callback
				request.BeginGetResponse(new AsyncCallback((asynchronousResult) =>
				                                           {
					HttpWebRequest asyncWebRequest = (HttpWebRequest)asynchronousResult.AsyncState;
					HttpWebResponse asyncWebResponse = (HttpWebResponse)asyncWebRequest.EndGetResponse(asynchronousResult);
					using (StreamReader streamReader = new StreamReader(asyncWebResponse.GetResponseStream()))
					{
						// Deserialize the result
						string jsonString = streamReader.ReadToEnd();
						Action<object> dummyCallback = obj => { };
						WrapResultBasedOnResponseType<string>(type, jsonString, new string[] { channelName }, false, 0, dummyCallback);
					}
				}), request
				                         
				                         );
				
				return true;
			}
			catch (System.Exception)
			{
				return false;
			}
		}
		
		private bool UrlProcessRequest<T>(Uri requestUri, RequestState<T> pubnubRequestState)
		{
			string channel = "";
			if (pubnubRequestState != null && pubnubRequestState.Channels != null)
			{
				channel = string.Join(",", pubnubRequestState.Channels);
			}
			
			try
			{
				if (!_channelRequest.ContainsKey(channel) && (pubnubRequestState.Type == ResponseType.Subscribe || pubnubRequestState.Type == ResponseType.Presence))
				{
					return false;
				}
				
				// Create Request
				PubnubWebRequestCreator requestCreator = new PubnubWebRequestCreator(_pubnubUnitTest);
				PubnubWebRequest request = (PubnubWebRequest)requestCreator.Create(requestUri);
				
#if (!SILVERLIGHT && !WINDOWS_PHONE)
				if (pubnubEnableProxyConfig && _pubnubProxy != null)
				{
					LoggingMethod.WriteToLog(string.Format("DateTime {0}, ProxyServer={1}; ProxyPort={2}; ProxyUserName={3}", DateTime.Now.ToString(), _pubnubProxy.ProxyServer, _pubnubProxy.ProxyPort, _pubnubProxy.ProxyUserName), LoggingMethod.LevelInfo);
					WebProxy webProxy = new WebProxy(_pubnubProxy.ProxyServer, _pubnubProxy.ProxyPort);
					webProxy.Credentials = new NetworkCredential(_pubnubProxy.ProxyUserName, _pubnubProxy.ProxyPassword);
					request.Proxy = webProxy;
				}
				
				request.Timeout = GetTimeoutInSecondsForResponseType(pubnubRequestState.Type) * 1000;
#endif
				
				pubnubRequestState.Request = request;
				
				if (pubnubRequestState.Type == ResponseType.Subscribe || pubnubRequestState.Type == ResponseType.Presence)
				{
					_channelRequest.AddOrUpdate(channel, pubnubRequestState.Request, (key, oldState) => pubnubRequestState.Request);
				}
				
				
				if (overrideTcpKeepAlive)
				{
					//Eventhough heart-beat is disabled, run one time to check internet connection by setting dueTime=0
					heartBeatTimer = new System.Threading.Timer(
						new TimerCallback(OnPubnubHeartBeatTimeoutCallback<T>), pubnubRequestState, 0,
						(-1 == _pubnubNetworkTcpCheckIntervalInSeconds) ? Timeout.Infinite : _pubnubNetworkTcpCheckIntervalInSeconds * 1000);
					_channelHeartbeatTimer.AddOrUpdate(requestUri, heartBeatTimer, (key, oldState) => heartBeatTimer);
				}
				else
				{
#if ((!__MonoCS__) && (!SILVERLIGHT) && !WINDOWS_PHONE)
					request.ServicePoint.SetTcpKeepAlive(true, _pubnubNetworkTcpCheckIntervalInSeconds * 1000, 1000);
#endif
				}
				LoggingMethod.WriteToLog(string.Format("DateTime {0}, Request={1}", DateTime.Now.ToString(), requestUri.ToString()), LoggingMethod.LevelInfo);
				
				
#if (__MonoCS__)
				if((pubnubRequestState.Type == ResponseType.Publish) && (RequestIsUnsafe(requestUri)))
				{
					SendRequestUsingTcpClient<T>(requestUri, pubnubRequestState);
				}
				else
				{
					IAsyncResult asyncResult = request.BeginGetResponse(new AsyncCallback(UrlProcessResponseCallback<T>), pubnubRequestState);
					if (!asyncResult.AsyncWaitHandle.WaitOne(GetTimeoutInSecondsForResponseType(pubnubRequestState.Type) * 1000))
					{
						OnPubnubWebRequestTimeout<T>(pubnubRequestState, true);
					}
				}
#elif (SILVERLIGHT || WINDOWS_PHONE)
				//For WP7, Ensure that the RequestURI length <= 1599
				//For SL, Ensure that the RequestURI length <= 1482 for Large Text Message. If RequestURI Length < 1343, Successful Publish occurs
				IAsyncResult asyncResult = request.BeginGetResponse(new AsyncCallback(UrlProcessResponseCallback<T>), pubnubRequestState);
				Timer webRequestTimer = new Timer(OnPubnubWebRequestTimeout<T>, pubnubRequestState, GetTimeoutInSecondsForResponseType(pubnubRequestState.Type) * 1000, Timeout.Infinite);
#else
				IAsyncResult asyncResult = request.BeginGetResponse(new AsyncCallback(UrlProcessResponseCallback<T>), pubnubRequestState);
				ThreadPool.RegisterWaitForSingleObject(asyncResult.AsyncWaitHandle, new WaitOrTimerCallback(OnPubnubWebRequestTimeout<T>), pubnubRequestState, GetTimeoutInSecondsForResponseType(pubnubRequestState.Type) * 1000, true);
#endif
				return true;
			}
			catch (System.Exception ex)
			{
				if (pubnubRequestState != null && pubnubRequestState.ErrorCallback != null)
				{
                    string multiChannel = (pubnubRequestState.Channels != null) ? string.Join(",", pubnubRequestState.Channels) : "";
                    PubnubErrorCode errorType = PubnubErrorCodeHelper.GetErrorType(ex);
                    int statusCode = (int)errorType;
                    string errorDescription = PubnubErrorCodeDescription.GetStatusCodeDescription(errorType);
                    PubnubClientError error = new PubnubClientError(statusCode, PubnubErrorSeverity.Warn, true, ex.Message, ex, PubnubMessageSource.Client, pubnubRequestState.Request, pubnubRequestState.Response, errorDescription, multiChannel);
                    GoToCallback(error, pubnubRequestState.ErrorCallback);

				}
				LoggingMethod.WriteToLog(string.Format("DateTime {0} Exception={1}", DateTime.Now.ToString(), ex.ToString()), LoggingMethod.LevelError);
				UrlRequestCommonExceptionHandler<T>(pubnubRequestState.Type, pubnubRequestState.Channels, false, pubnubRequestState.UserCallback, pubnubRequestState.ConnectCallback, pubnubRequestState.ErrorCallback, false);
				return false;
			}
		}
		
#if (__MonoCS__)
		bool RequestIsUnsafe(Uri requestUri)
		{
			bool isUnsafe = false;
			StringBuilder requestMessage = new StringBuilder();
			if (requestUri.Segments.Length > 7)
			{
				for (int i = 7; i < requestUri.Segments.Length; i++)
				{
					requestMessage.Append(requestUri.Segments[i]);
				}
			}
			foreach (char ch in requestMessage.ToString().ToCharArray())
			{
				if (" ~`!@#$^&*()+=[]\\{}|;':\"./<>?".IndexOf(ch) >= 0)
				{
					isUnsafe = true;
					break;
				}
			}
			return isUnsafe;
		}
		
		string CreateRequest(Uri requestUri)
		{
			StringBuilder requestBuilder = new StringBuilder();
			requestBuilder.Append("GET ");
			requestBuilder.Append(requestUri.OriginalString);
			
			if (ssl)
			{
				requestBuilder.Append(string.Format(" HTTP/1.1\r\nConnection: close\r\nHost: {0}:443\r\n\r\n", this.domainName));
			}
			else
			{
				requestBuilder.Append(string.Format(" HTTP/1.1\r\nConnection: close\r\nHost: {0}:80\r\n\r\n", this.domainName));
			}
			return requestBuilder.ToString();
		}
		
		void ConnectToHostAndSendRequest<T>(bool sslEnabled, TcpClient tcpClient, RequestState<T> pubnubRequestState, string requestString)
		{
			NetworkStream stream = tcpClient.GetStream();
			
			string proxyAuth = string.Format("{0}:{1}", _pubnubProxy.ProxyUserName, _pubnubProxy.ProxyPassword);
			byte[] proxyAuthBytes = Encoding.UTF8.GetBytes(proxyAuth);
			
			//Proxy-Authenticate: authentication mode Basic, Digest and NTLM
			string connectRequest = "";
			if (sslEnabled)
			{
				connectRequest = string.Format("CONNECT {0}:443  HTTP/1.1\r\nProxy-Authorization: Basic {1}\r\nHost: {0}\r\n\r\n", this.domainName, Convert.ToBase64String(proxyAuthBytes));
			}
			else
			{
				connectRequest = string.Format("CONNECT {0}:80  HTTP/1.1\r\nProxy-Authorization: Basic {1}\r\nHost: {0}\r\n\r\n", this.domainName, Convert.ToBase64String(proxyAuthBytes));
			}
			
			byte[] tunnelRequest = Encoding.UTF8.GetBytes(connectRequest);
			stream.Write(tunnelRequest, 0, tunnelRequest.Length);
			stream.Flush();
			
			stream.ReadTimeout = pubnubRequestState.Request.Timeout * 5;
			
			StateObject<T> state = new StateObject<T>();
			state.tcpClient = tcpClient;
			state.RequestState = pubnubRequestState;
			state.requestString = requestString;
			state.netStream = stream;
			
			//stream.BeginRead(state.buffer, 0, state.buffer.Length, new AsyncCallback(ConnectToHostAndSendRequestCallback<T>), state);
			
			StringBuilder response = new StringBuilder();
			var responseStream = new StreamReader(stream);
			
			char[] buffer = new char[2048];
			
			int charsRead = responseStream.Read(buffer, 0, buffer.Length);
			bool connEstablished = false;
			while (charsRead > 0)
			{
				response.Append(buffer);
				if ((response.ToString().IndexOf("200 Connection established") > 0) || (response.ToString().IndexOf("200 OK") > 0))
				{
					connEstablished = true;
					break;
				}
				charsRead = responseStream.Read(buffer, 0, buffer.Length);
			}
			
			if (connEstablished)
			{
				if (sslEnabled)
				{
					SendSslRequest<T>(stream, tcpClient, pubnubRequestState, requestString);
				}
				else
				{
					SendRequest<T>(tcpClient, pubnubRequestState, requestString);
				}
				
			}
			else if (response.ToString().IndexOf("407 Proxy Authentication Required") > 0)
			{
				int pos = response.ToString().IndexOf("Proxy-Authenticate");
				string desc = "";
				if (pos > 0)
				{
					desc = response.ToString().Substring(pos, response.ToString().IndexOf("\r\n", pos) - pos);
				}
				throw new WebException(string.Format("Proxy Authentication Required. Desc: {0}", desc));
			}
			else
			{
				throw new WebException("Couldn't connect to the server");
			}
		}
		
#if(MONODROID || __ANDROID__)      
		/// <summary>
		/// Workaround for the bug described here 
		/// https://bugzilla.xamarin.com/show_bug.cgi?id=6501
		/// </summary>
		/// <param name="sender">Sender.</param>
		/// <param name="certificate">Certificate.</param>
		/// <param name="chain">Chain.</param>
		/// <param name="sslPolicyErrors">Ssl policy errors.</param>
		static bool Validator (object sender,
		                       System.Security.Cryptography.X509Certificates.X509Certificate
		                       certificate,
		                       System.Security.Cryptography.X509Certificates.X509Chain chain,
		                       System.Net.Security.SslPolicyErrors sslPolicyErrors)
		{
			var sslTrustManager = (IX509TrustManager) typeof (AndroidEnvironment)
				.GetField ("sslTrustManager",
				           System.Reflection.BindingFlags.NonPublic |
				           System.Reflection.BindingFlags.Static)
					.GetValue (null);
			
			Func<Java.Security.Cert.CertificateFactory,
			System.Security.Cryptography.X509Certificates.X509Certificate,
			Java.Security.Cert.X509Certificate> c = (f, v) =>
				f.GenerateCertificate (
					new System.IO.MemoryStream (v.GetRawCertData ()))
					.JavaCast<Java.Security.Cert.X509Certificate>();
			var cFactory = Java.Security.Cert.CertificateFactory.GetInstance (Javax.Net.Ssl.TrustManagerFactory.DefaultAlgorithm);
			var certs = new List<Java.Security.Cert.X509Certificate>(
				chain.ChainElements.Count + 1);
			certs.Add (c (cFactory, certificate));
			foreach (var ce in chain.ChainElements) {
				if (certificate.Equals (ce.Certificate))
					continue;
				certificate = ce.Certificate;
				certs.Add (c (cFactory, certificate));
			}
			try {
				//had to comment this out as sslTrustManager was returning null
				//working on the fix or a workaround
				//sslTrustManager.CheckServerTrusted (certs.ToArray (),
				//                                  Javax.Net.Ssl.TrustManagerFactory.DefaultAlgorithm);
				return true;
			}
			catch (Exception e) {
				throw new Exception("SSL error");
			}
		}
#endif
		
#if(UNITY_ANDROID)      
		/// <summary>
		/// Workaround for the bug described here 
		/// https://bugzilla.xamarin.com/show_bug.cgi?id=6501
		/// </summary>
		/// <param name="sender">Sender.</param>
		/// <param name="certificate">Certificate.</param>
		/// <param name="chain">Chain.</param>
		/// <param name="sslPolicyErrors">Ssl policy errors.</param>
		static bool ValidatorUnity (object sender,
		                            System.Security.Cryptography.X509Certificates.X509Certificate
		                            certificate,
		                            System.Security.Cryptography.X509Certificates.X509Chain chain,
		                            System.Net.Security.SslPolicyErrors sslPolicyErrors)
		{
			//TODO:
			return true;
		}
#endif
		
		private void ConnectToHostAndSendRequestCallback<T>(IAsyncResult asynchronousResult)
		{
			StateObject<T> asynchStateObject = asynchronousResult.AsyncState as StateObject<T>;
			RequestState<T> asynchRequestState = asynchStateObject.RequestState;
			
			string channels = "";
			if (asynchRequestState != null && asynchRequestState.Channels != null)
			{
				channels = string.Join(",", asynchRequestState.Channels);
			}
			
			try
			{
				string requestString = asynchStateObject.requestString;
				TcpClient tcpClient = asynchStateObject.tcpClient;
				
				NetworkStream netStream = asynchStateObject.netStream;
				int bytesRead = netStream.EndRead(asynchronousResult);
				
				if (bytesRead > 0)
				{
					asynchStateObject.sb.Append(Encoding.ASCII.GetString(asynchStateObject.buffer, 0, bytesRead));
					
					netStream.BeginRead(asynchStateObject.buffer, 0, StateObject<T>.BufferSize,
					                    new AsyncCallback(ConnectToHostAndSendRequestCallback<T>), asynchStateObject);
				}
				else
				{
					string resp = asynchStateObject.sb.ToString();
					if (resp.IndexOf("200 Connection established") > 0)
					{
						SendSslRequest<T>(netStream, tcpClient, asynchRequestState, requestString);
					}
					else
					{
						throw new WebException("Couldn't connect to the server");
					}
				}
			}
			catch (WebException webEx)
			{
				if (asynchRequestState != null && asynchRequestState.ErrorCallback != null)
				{
					//TODO: Identify refactoring
					List<object> errorResult = new List<object>();
					string errorJsonString = string.Format("[2, \"{0}\"]", webEx.ToString().Replace("\r\n", " ").Replace("\r", " ").Replace("\n", " ").Replace("\\", "\\\\").Replace("\"", "\\\""));
					errorResult = _jsonPluggableLibrary.DeserializeToListOfObject(errorJsonString);
					errorResult.Add(channels);
					GoToCallback<T>(errorResult, asynchRequestState.ErrorCallback);
				}
				ProcessResponseCallbackWebExceptionHandler<T>(webEx, asynchRequestState, channels);
			}
			catch (Exception ex)
			{
				if (asynchRequestState != null && asynchRequestState.ErrorCallback != null)
				{
					//TODO: Identify refactoring
					List<object> errorResult = new List<object>();
					string errorJsonString = string.Format("[2, \"{0}\"]", ex.ToString().Replace("\r\n", " ").Replace("\r", " ").Replace("\n", " ").Replace("\\", "\\\\").Replace("\"", "\\\""));
					errorResult = _jsonPluggableLibrary.DeserializeToListOfObject(errorJsonString);
					errorResult.Add(channels);
					GoToCallback<T>(errorResult, asynchRequestState.ErrorCallback);
				}
				ProcessResponseCallbackExceptionHandler<T>(ex, asynchRequestState);
			}
		}
		
		void SendSslRequest<T>(NetworkStream netStream, TcpClient tcpClient, RequestState<T> pubnubRequestState, string requestString)
		{
#if(MONODROID || __ANDROID__)
			SslStream sslStream = new SslStream(netStream, true, Validator, null);
#elif(UNITY_ANDROID)
			ServicePointManager.ServerCertificateValidationCallback = ValidatorUnity;
			SslStream sslStream = new SslStream(netStream, true, ValidatorUnity, null);
#else
			SslStream sslStream = new SslStream(netStream);
#endif
			StateObject<T> state = new StateObject<T>();
			state.tcpClient = tcpClient;
			state.sslns = sslStream;
			state.RequestState = pubnubRequestState;
			state.requestString = requestString;
			sslStream.AuthenticateAsClient(this.domainName);
			AfterAuthentication(state);
		}
		
		void AfterAuthentication<T> (StateObject<T> state)
		{
			SslStream sslStream = state.sslns;
			byte[] sendBuffer = UTF8Encoding.UTF8.GetBytes(state.requestString);
			
			sslStream.Write(sendBuffer);
			sslStream.Flush();
#if(!MONODROID && !__ANDROID__ && !UNITY_ANDROID)         
			sslStream.ReadTimeout = state.RequestState.Request.Timeout;
#endif
			sslStream.BeginRead(state.buffer, 0, state.buffer.Length, new AsyncCallback(SendRequestUsingTcpClientCallback<T>), state);
		}
		
		private void SendSslRequestAuthenticationCallback<T>(IAsyncResult asynchronousResult)
		{
			StateObject<T> state = asynchronousResult.AsyncState as StateObject<T>;
			RequestState<T> asynchRequestState = state.RequestState;
			string channels = "";
			if (asynchRequestState != null && asynchRequestState.Channels != null)
			{
				channels = string.Join(",", asynchRequestState.Channels);
			}
			try{
				AfterAuthentication(state);
			}
			catch (WebException webEx)
			{
				if (asynchRequestState != null && asynchRequestState.ErrorCallback != null)
				{
					//TODO: Identify refactoring
					List<object> errorResult = new List<object>();
					string errorJsonString = string.Format("[2, \"{0}\"]", webEx.ToString().Replace("\r\n", " ").Replace("\r", " ").Replace("\n", " ").Replace("\\", "\\\\").Replace("\"", "\\\""));
					errorResult = _jsonPluggableLibrary.DeserializeToListOfObject(errorJsonString);
					errorResult.Add(channels);
					GoToCallback<T>(errorResult, asynchRequestState.ErrorCallback);
				}
				ProcessResponseCallbackWebExceptionHandler<T>(webEx, asynchRequestState, channels);
			}
			catch (Exception ex)
			{
				if (asynchRequestState != null && asynchRequestState.ErrorCallback != null)
				{
					//TODO: Identify refactoring
					List<object> errorResult = new List<object>();
					string errorJsonString = string.Format("[2, \"{0}\"]", ex.ToString().Replace("\r\n", " ").Replace("\r", " ").Replace("\n", " ").Replace("\\", "\\\\").Replace("\"", "\\\""));
					errorResult = _jsonPluggableLibrary.DeserializeToListOfObject(errorJsonString);
					errorResult.Add(channels);
					GoToCallback<T>(errorResult, asynchRequestState.ErrorCallback);
				}
				ProcessResponseCallbackExceptionHandler<T>(ex, asynchRequestState);
			}
		}
		
		void SendRequest<T>(TcpClient tcpClient, RequestState<T> pubnubRequestState, string requestString)
		{
			NetworkStream netStream = tcpClient.GetStream();
			
			StateObject<T> state = new StateObject<T>();
			state.tcpClient = tcpClient;
			state.netStream = netStream;
			state.RequestState = pubnubRequestState;
			
			System.IO.StreamWriter streamWriter = new System.IO.StreamWriter(netStream);
			streamWriter.Write(requestString);
			streamWriter.Flush();
#if(!MONODROID && !__ANDROID__ && !UNITY_ANDROID)
			netStream.ReadTimeout = pubnubRequestState.Request.Timeout;
#endif
			netStream.BeginRead(state.buffer, 0, state.buffer.Length, new AsyncCallback(SendRequestUsingTcpClientCallback<T>), state);
			
		}
		
		private void SendRequestUsingTcpClient<T>(Uri requestUri, RequestState<T> pubnubRequestState)
		{
			TcpClient tcpClient = new TcpClient();
			tcpClient.NoDelay = false;
#if(!MONODROID && !__ANDROID__ && !UNITY_ANDROID)
			tcpClient.SendTimeout = pubnubRequestState.Request.Timeout;
#endif          
			
			string requestString = CreateRequest(requestUri);
			
			if (ssl)
			{
				if (pubnubEnableProxyConfig && _pubnubProxy != null)
				{
					tcpClient.Connect(_pubnubProxy.ProxyServer, _pubnubProxy.ProxyPort);
					
					ConnectToHostAndSendRequest<T>(ssl, tcpClient, pubnubRequestState, requestString);
				}
				else
				{
					tcpClient.Connect(this.domainName, 443);
					NetworkStream netStream = tcpClient.GetStream();
					SendSslRequest<T>(netStream, tcpClient, pubnubRequestState, requestString);
				}
			}
			else
			{
				if (pubnubEnableProxyConfig && _pubnubProxy != null)
				{
					tcpClient.Connect(_pubnubProxy.ProxyServer, _pubnubProxy.ProxyPort);
					
					ConnectToHostAndSendRequest(ssl, tcpClient, pubnubRequestState, requestString);
				}
				else
				{
					tcpClient.Connect(this.domainName, 80);
					SendRequest<T>(tcpClient, pubnubRequestState, requestString);
				}
			}
		}
		
		private void SendRequestUsingTcpClientCallback<T>(IAsyncResult asynchronousResult)
		{
			StateObject<T> state = asynchronousResult.AsyncState as StateObject<T>;
			RequestState<T> asynchRequestState = state.RequestState;
			string channel = "";
			if (asynchRequestState != null && asynchRequestState.Channels != null)
			{
				channel = string.Join(",", asynchRequestState.Channels);
			}
			try
			{
				//StateObject<T> state = (StateObject<T>) asynchronousResult.AsyncState;
				if (ssl)
				{
					SslStream sslns = state.sslns;
					int bytesRead = sslns.EndRead(asynchronousResult);
					
					if (bytesRead > 0)
					{
						Decoder decoder = Encoding.UTF8.GetDecoder();
						char[] chars = new char[decoder.GetCharCount(state.buffer, 0, bytesRead)];
						decoder.GetChars(state.buffer, 0, bytesRead, chars, 0);
						state.sb.Append(chars);
						
						sslns.BeginRead(state.buffer, 0, StateObject<T>.BufferSize,
						                new AsyncCallback(SendRequestUsingTcpClientCallback<T>), state);
					}
					else
					{
						HandleTcpClientResponse(state, asynchRequestState, channel, asynchronousResult);
					}
				}
				else
				{
					NetworkStream netStream = state.netStream;
					int bytesRead = netStream.EndRead(asynchronousResult);
					
					if (bytesRead > 0)
					{
						state.sb.Append(Encoding.ASCII.GetString(state.buffer, 0, bytesRead));
						
						netStream.BeginRead(state.buffer, 0, StateObject<T>.BufferSize,
						                    new AsyncCallback(SendRequestUsingTcpClientCallback<T>), state);
					}
					else
					{
						HandleTcpClientResponse(state, asynchRequestState, channel, asynchronousResult);
					}
				}
			}
			catch (WebException webEx)
			{
				ProcessResponseCallbackWebExceptionHandler<T>(webEx, asynchRequestState, channel);
			}
			catch (Exception ex)
			{
				ProcessResponseCallbackExceptionHandler<T>(ex, asynchRequestState);
			}
		}
		
		void HandleTcpClientResponse<T>(StateObject<T> state, RequestState<T> asynchRequestState, string channel, IAsyncResult asynchronousResult)
		{
			List<object> result = new List<object>();
			if (state.sb.Length > 1)
			{
				string jsonString = ParseResponse<T>(state.sb.ToString(), asynchronousResult);
				LoggingMethod.WriteToLog(string.Format("DateTime {0}, JSON for channel={1} ({2}) ={3}", DateTime.Now.ToString(), channel, asynchRequestState.Type.ToString(), jsonString), LoggingMethod.LevelInfo);
				
				if (overrideTcpKeepAlive)
				{
					TerminateHeartbeatTimer(state.RequestState.Request.RequestUri);
				}
				
				if (jsonString != null && !string.IsNullOrEmpty(jsonString) && !string.IsNullOrEmpty(channel.Trim()))
				{
					result = WrapResultBasedOnResponseType(asynchRequestState.Type, jsonString, asynchRequestState.Channels, asynchRequestState.Reconnect, asynchRequestState.Timetoken, asynchRequestState.ErrorCallback);
				}
				
				ProcessResponseCallbacks<T>(result, asynchRequestState);
			}
			if (state.tcpClient != null)
				state.tcpClient.Close();
		}
		
		string ParseResponse<T>(string responseString, IAsyncResult asynchronousResult)
		{
			string json = "";
			int pos = responseString.LastIndexOf('\n');
			if ((responseString.StartsWith("HTTP/1.1 ") || responseString.StartsWith("HTTP/1.0 "))
			    && (pos != -1) && responseString.Length >= pos + 1)
				
			{
				json = responseString.Substring(pos + 1);
			}
			return json;
		}
		
#endif

        void ProcessResponseCallbackExceptionHandler<T>(Exception ex, RequestState<T> asynchRequestState)
		{
			//common Exception handler
			if (asynchRequestState.Response != null)
				asynchRequestState.Response.Close();
			
			LoggingMethod.WriteToLog(string.Format("DateTime {0} Exception= {1} for URL: {2}", DateTime.Now.ToString(), ex.ToString(), asynchRequestState.Request.RequestUri.ToString()), LoggingMethod.LevelError);
			UrlRequestCommonExceptionHandler<T>(asynchRequestState.Type, asynchRequestState.Channels, asynchRequestState.Timeout, asynchRequestState.UserCallback, asynchRequestState.ConnectCallback, asynchRequestState.ErrorCallback, false);
		}
		
		void ProcessResponseCallbackWebExceptionHandler<T>(WebException webEx, RequestState<T> asynchRequestState, string channel)
		{
			bool reconnect = false;
			LoggingMethod.WriteToLog(string.Format("DateTime {0}, WebException: {1} for URL: {2}", DateTime.Now.ToString(), webEx.ToString(), asynchRequestState.Request.RequestUri.ToString()), LoggingMethod.LevelError);
			
			if (asynchRequestState.Response != null) asynchRequestState.Response.Close();
			if (asynchRequestState.Request != null) asynchRequestState.Request.Abort();
			
#if (!SILVERLIGHT)
			if ((webEx.Status == WebExceptionStatus.NameResolutionFailure //No network
			     || webEx.Status == WebExceptionStatus.ConnectFailure //Sending Keep-alive packet failed (No network)/Server is down.
			     || webEx.Status == WebExceptionStatus.ServerProtocolViolation//Problem with proxy or ISP
			     || webEx.Status == WebExceptionStatus.ProtocolError
			     ) && (!overrideTcpKeepAlive))
			{
				//internet connection problem.
				LoggingMethod.WriteToLog(string.Format("DateTime {0}, _urlRequest - Internet connection problem", DateTime.Now.ToString()), LoggingMethod.LevelError);
				
				if (_channelInternetStatus.ContainsKey(channel)
				    && (asynchRequestState.Type == ResponseType.Subscribe || asynchRequestState.Type == ResponseType.Presence))
				{
					reconnect = true;
					
					if (_channelInternetStatus[channel])
					{
						//Reset Retry if previous state is true
						_channelInternetRetry.AddOrUpdate(channel, 0, (key, oldValue) => 0);
					}
					else
					{
						_channelInternetRetry.AddOrUpdate(channel, 1, (key, oldValue) => oldValue + 1);
                        string multiChannel = (asynchRequestState.Channels != null) ? string.Join(",", asynchRequestState.Channels) : "";
                        LoggingMethod.WriteToLog(string.Format("DateTime {0} {1} channel = {2} _urlRequest - Internet connection retry {3} of {4}", DateTime.Now.ToString(), asynchRequestState.Type, multiChannel, _channelInternetRetry[channel], _pubnubNetworkCheckRetries), LoggingMethod.LevelInfo);

                        string message = string.Format("Detected internet connection problem. Retrying connection attempt {0} of {1}", _channelInternetRetry[channel], _pubnubNetworkCheckRetries);
                        PubnubClientError error = new PubnubClientError(0, PubnubErrorSeverity.Info, message, PubnubMessageSource.Client, multiChannel);
                        GoToCallback(error, asynchRequestState.ErrorCallback);
					}
					_channelInternetStatus[channel] = false;
					
					Thread.Sleep(_pubnubWebRequestRetryIntervalInSeconds * 1000);
				}
			}
#endif
			UrlRequestCommonExceptionHandler<T>(asynchRequestState.Type, asynchRequestState.Channels, asynchRequestState.Timeout,
			                                    asynchRequestState.UserCallback, asynchRequestState.ConnectCallback, asynchRequestState.ErrorCallback, reconnect);
		}
		
		void ProcessResponseCallbacks<T>(List<object> result, RequestState<T> asynchRequestState)
		{
			if (result != null && result.Count >= 1 && asynchRequestState.UserCallback != null)
			{
				ResponseToConnectCallback<T>(result, asynchRequestState.Type, asynchRequestState.Channels, asynchRequestState.ConnectCallback);
				ResponseToUserCallback<T>(result, asynchRequestState.Type, asynchRequestState.Channels, asynchRequestState.UserCallback);
			}
		}
		
		private void UrlProcessResponseCallback<T>(IAsyncResult asynchronousResult)
		{
			List<object> result = new List<object>();
			
			RequestState<T> asynchRequestState = asynchronousResult.AsyncState as RequestState<T>;
			
			string channel="";
			if (asynchRequestState != null && asynchRequestState.Channels != null)
			{
				channel = string.Join(",", asynchRequestState.Channels);
			}
			
			PubnubWebRequest asyncWebRequest = asynchRequestState.Request as PubnubWebRequest;
            try
            {
                if (asyncWebRequest != null)
                {
                    using (PubnubWebResponse asyncWebResponse = (PubnubWebResponse)asyncWebRequest.EndGetResponse(asynchronousResult))
                    {
                        asynchRequestState.Response = asyncWebResponse;

                        using (StreamReader streamReader = new StreamReader(asyncWebResponse.GetResponseStream()))
                        {
                            if (asynchRequestState.Type == ResponseType.Subscribe || asynchRequestState.Type == ResponseType.Presence)
                            {
                                if (!overrideTcpKeepAlive && _channelInternetStatus.ContainsKey(channel) && !_channelInternetStatus[channel])
                                {
                                    if (asynchRequestState.Channels != null)
                                    {
                                        for (int index = 0; index < asynchRequestState.Channels.Length; index++)
                                        {
                                            string activeChannel = asynchRequestState.Channels[index].ToString();

                                            string status = "Internet connection available";

                                            if (_channelCallbacks.Count > 0 && _channelCallbacks.ContainsKey(activeChannel))
                                            {
                                                object callbackObject;
                                                bool channelAvailable = _channelCallbacks.TryGetValue(activeChannel, out callbackObject);
                                                PubnubChannelCallback<T> currentPubnubCallback = null;
                                                if (channelAvailable)
                                                {
                                                    currentPubnubCallback = callbackObject as PubnubChannelCallback<T>;
                                                }

                                                if (currentPubnubCallback != null && currentPubnubCallback.ConnectCallback != null)
                                                {
                                                    PubnubErrorCode errorType = PubnubErrorCode.YesInternet;
                                                    int statusCode = (int)errorType;
                                                    string errorDescription = PubnubErrorCodeDescription.GetStatusCodeDescription(errorType);

                                                    PubnubClientError error = new PubnubClientError(statusCode, PubnubErrorSeverity.Info, status, PubnubMessageSource.Client, null, null, errorDescription, activeChannel);
                                                    GoToCallback(error, currentPubnubCallback.ErrorCallback);
                                                }
                                            }
                                        }
                                    }
                                }

                                _channelInternetStatus.AddOrUpdate(channel, true, (key, oldValue) => true);
                            }

                            //Deserialize the result
                            string jsonString = streamReader.ReadToEnd();
                            streamReader.Close();

                            LoggingMethod.WriteToLog(string.Format("DateTime {0}, JSON for channel={1} ({2}) ={3}", DateTime.Now.ToString(), channel, asynchRequestState.Type.ToString(), jsonString), LoggingMethod.LevelInfo);

                            if (overrideTcpKeepAlive)
                            {
                                TerminateHeartbeatTimer(asyncWebRequest.RequestUri);
                            }

                            result = WrapResultBasedOnResponseType<T>(asynchRequestState.Type, jsonString, asynchRequestState.Channels, asynchRequestState.Reconnect, asynchRequestState.Timetoken, asynchRequestState.ErrorCallback);
                        }
                        asyncWebResponse.Close();
                    }
                }
                else
                {
                    LoggingMethod.WriteToLog(string.Format("DateTime {0}, Request aborted for channel={1}", DateTime.Now.ToString(), channel), LoggingMethod.LevelInfo);
                }

                ProcessResponseCallbacks<T>(result, asynchRequestState);

                if ((asynchRequestState.Type == ResponseType.Subscribe || asynchRequestState.Type == ResponseType.Presence) && (asynchRequestState.Channels != null) && (result.Count > 0))
                {
                    foreach (string currentChannel in asynchRequestState.Channels)
                    {
                        _multiChannelSubscribe.AddOrUpdate(currentChannel, Convert.ToInt64(result[1].ToString()), (key, oldValue) => Convert.ToInt64(result[1].ToString()));
                    }
                }

                switch (asynchRequestState.Type)
                {
                    case ResponseType.Subscribe:
                    case ResponseType.Presence:
                        MultiplexInternalCallback<T>(asynchRequestState.Type, result, asynchRequestState.UserCallback, asynchRequestState.ConnectCallback, asynchRequestState.ErrorCallback);
                        break;
                    default:
                        break;
                }
            }
            catch (WebException webEx)
            {
                HttpStatusCode currentHttpStatusCode;
                if (webEx.Response != null && asynchRequestState != null)
                {
                    if (webEx.Response.GetType().ToString() == "System.Net.HttpWebResponse"
                        || webEx.Response.GetType().ToString() == "System.Net.Browser.ClientHttpWebResponse")
                    {
                        currentHttpStatusCode = ((HttpWebResponse)webEx.Response).StatusCode;
                    }
                    else
                    {
                        currentHttpStatusCode = ((PubnubWebResponse)webEx.Response).HttpStatusCode;
                    }
                    PubnubWebResponse exceptionResponse = new PubnubWebResponse(webEx.Response, currentHttpStatusCode);
                    if (exceptionResponse != null)
                    {
                        asynchRequestState.Response = exceptionResponse;

                        using (StreamReader streamReader = new StreamReader(asynchRequestState.Response.GetResponseStream()))
                        {
                            string jsonString = streamReader.ReadToEnd();

                            streamReader.Close();

                            LoggingMethod.WriteToLog(string.Format("DateTime {0}, JSON for channel={1} ({2}) ={3}", DateTime.Now.ToString(), channel, asynchRequestState.Type.ToString(), jsonString), LoggingMethod.LevelInfo);

                            if (overrideTcpKeepAlive)
                            {
                                TerminateHeartbeatTimer(asyncWebRequest.RequestUri);
                            }

                            //currentStatusCode = HttpStatusCode.BadGateway;
                            //if (((int)currentStatusCode < 200 || (int)currentStatusCode >= 300) && ((int)currentStatusCode != 400))
                            if ((int)currentHttpStatusCode < 200 || (int)currentHttpStatusCode >= 300)
                            {
                                result = null;
                                string errorDescription = "";
                                int pubnubStatusCode = 0;

                                if ((int)currentHttpStatusCode == 500 || (int)currentHttpStatusCode == 502 || (int)currentHttpStatusCode == 504 || (int)currentHttpStatusCode == 414)
                                {
                                    //This status code is not giving json string.
                                    string statusMessage = currentHttpStatusCode.ToString();
                                    PubnubErrorCode pubnubErrorType = PubnubErrorCodeHelper.GetErrorType((int)currentHttpStatusCode, statusMessage);
                                    pubnubStatusCode = (int)pubnubErrorType;
                                    errorDescription = PubnubErrorCodeDescription.GetStatusCodeDescription(pubnubErrorType);
                                }
                                else if (_jsonPluggableLibrary.IsArrayCompatible(jsonString))
                                {
                                    List<object> deserializeStatus = _jsonPluggableLibrary.DeserializeToListOfObject(jsonString);
                                    string statusMessage = deserializeStatus[1].ToString();
                                    PubnubErrorCode pubnubErrorType = PubnubErrorCodeHelper.GetErrorType((int)currentHttpStatusCode, statusMessage);
                                    pubnubStatusCode = (int)pubnubErrorType;
                                    errorDescription = PubnubErrorCodeDescription.GetStatusCodeDescription(pubnubErrorType);
                                }
                                else if (_jsonPluggableLibrary.IsDictionaryCompatible(jsonString))
                                {
                                    Dictionary<string, object> deserializeStatus = _jsonPluggableLibrary.DeserializeToDictionaryOfObject(jsonString);
                                    string statusMessage = deserializeStatus["message"].ToString();
                                    PubnubErrorCode pubnubErrorType = PubnubErrorCodeHelper.GetErrorType((int)currentHttpStatusCode, statusMessage);
                                    pubnubStatusCode = (int)pubnubErrorType;
                                    errorDescription = PubnubErrorCodeDescription.GetStatusCodeDescription(pubnubErrorType);
                                }

                                PubnubClientError error = new PubnubClientError(pubnubStatusCode, PubnubErrorSeverity.Critical, jsonString, PubnubMessageSource.Server, asynchRequestState.Request, asynchRequestState.Response, errorDescription, channel);
                                GoToCallback(error, asynchRequestState.ErrorCallback);
                            }
                            else if (jsonString != "[]")
                            {
                                result = WrapResultBasedOnResponseType<T>(asynchRequestState.Type, jsonString, asynchRequestState.Channels, asynchRequestState.Reconnect, asynchRequestState.Timetoken, asynchRequestState.ErrorCallback);
                            }
                            else
                            {
                                result = null;
                            }
                        }
                    }
                    exceptionResponse.Close();

                    if (result != null && result.Count > 0)
                    {
                        ProcessResponseCallbacks<T>(result, asynchRequestState);
                    }
                }
                else
                {
                    //TODO:
                    PubnubErrorCode errorType = PubnubErrorCodeHelper.GetErrorType(webEx.Status);
                    int statusCode = (int)errorType;
                    string errorDescription = PubnubErrorCodeDescription.GetStatusCodeDescription(errorType);
                    if (asynchRequestState.Channels != null || asynchRequestState.Type == ResponseType.Time)
                    {
                        if (asynchRequestState.Type == ResponseType.Subscribe
                            || asynchRequestState.Type == ResponseType.Presence)
                        {
                            if (webEx.Message.IndexOf("The request was aborted: The request was canceled") == -1)
                            {
                                for (int index = 0; index < asynchRequestState.Channels.Length; index++)
                                {
                                    string activeChannel = asynchRequestState.Channels[index].ToString();
                                    if (_channelCallbacks.Count > 0 && _channelCallbacks.ContainsKey(activeChannel))
                                    {
                                        object callbackObject;
                                        bool channelAvailable = _channelCallbacks.TryGetValue(activeChannel, out callbackObject);
                                        PubnubChannelCallback<T> currentPubnubCallback = null;
                                        if (channelAvailable)
                                        {
                                            currentPubnubCallback = callbackObject as PubnubChannelCallback<T>;
                                        }
                                        if (currentPubnubCallback != null && currentPubnubCallback.ErrorCallback != null)
                                        {
                                            PubnubClientError error = new PubnubClientError(statusCode, PubnubErrorSeverity.Warn, true, webEx.Message, webEx, PubnubMessageSource.Client, asynchRequestState.Request, asynchRequestState.Response, errorDescription, activeChannel);
                                            LoggingMethod.WriteToLog(string.Format("DateTime {0}, PubnubClientError = {1}", DateTime.Now.ToString(), error.ToString()), LoggingMethod.LevelInfo);
                                            GoToCallback(error, currentPubnubCallback.ErrorCallback);
                                        }
                                    }

                                }
                            }
                        }
                        else
                        {
                            PubnubClientError error = new PubnubClientError(statusCode, PubnubErrorSeverity.Warn, true, webEx.Message, webEx, PubnubMessageSource.Client, asynchRequestState.Request, asynchRequestState.Response, errorDescription, channel);
                            LoggingMethod.WriteToLog(string.Format("DateTime {0}, PubnubClientError = {1}", DateTime.Now.ToString(), error.ToString()), LoggingMethod.LevelInfo);
                            GoToCallback(error, asynchRequestState.ErrorCallback);
                        }
                    }
                    ProcessResponseCallbackWebExceptionHandler<T>(webEx, asynchRequestState, channel);
                }
            }
            catch (Exception ex)
            {
                if (asynchRequestState.Channels != null)
                {
                    if (asynchRequestState.Type == ResponseType.Subscribe
                        || asynchRequestState.Type == ResponseType.Presence)
                    {
                        for (int index = 0; index < asynchRequestState.Channels.Length; index++)
                        {
                            string activeChannel = asynchRequestState.Channels[index].ToString();

                            if (_channelCallbacks.Count > 0 && _channelCallbacks.ContainsKey(activeChannel))
                            {
                                object callbackObject;
                                bool channelAvailable = _channelCallbacks.TryGetValue(activeChannel, out callbackObject);
                                PubnubChannelCallback<T> currentPubnubCallback = null;
                                if (channelAvailable)
                                {
                                    currentPubnubCallback = callbackObject as PubnubChannelCallback<T>;
                                }
                                if (currentPubnubCallback != null && currentPubnubCallback.ErrorCallback != null)
                                {
                                    PubnubErrorCode errorType = PubnubErrorCodeHelper.GetErrorType(ex);
                                    int statusCode = (int)errorType;
                                    string errorDescription = PubnubErrorCodeDescription.GetStatusCodeDescription(errorType);
                                    PubnubClientError error = new PubnubClientError(statusCode, PubnubErrorSeverity.Warn, true, ex.Message, ex, PubnubMessageSource.Client, asynchRequestState.Request, asynchRequestState.Response, errorDescription, activeChannel);
                                    GoToCallback(error, currentPubnubCallback.ErrorCallback);
                                }
                            }
                        }
                    }
                    else
                    {
                        //TODO: Identify refactoring
                        //List<object> errorResult = new List<object>();
                        //string jsonString = string.Format("[2, \"{0}\"]", ex.ToString().Replace("\r\n", " ").Replace("\r", " ").Replace("\n", " ").Replace("\\", "\\\\").Replace("\"", "\\\""));
                        //errorResult = _jsonPluggableLibrary.DeserializeToListOfObject(jsonString);
                        //errorResult.Add(channel);
                        PubnubErrorCode errorType = PubnubErrorCodeHelper.GetErrorType(ex);
                        int statusCode = (int)errorType;
                        string errorDescription = PubnubErrorCodeDescription.GetStatusCodeDescription(errorType);
                        PubnubClientError error = new PubnubClientError(statusCode, PubnubErrorSeverity.Warn, true, ex.Message, ex, PubnubMessageSource.Client, asynchRequestState.Request, asynchRequestState.Response, errorDescription, channel);
                        GoToCallback(error, asynchRequestState.ErrorCallback);
                    }

                }

                ProcessResponseCallbackExceptionHandler<T>(ex, asynchRequestState);
            }
		}
		
		private void VerifyOrSetSessionUUID()
		{
			if (string.IsNullOrEmpty(this.sessionUUID) || string.IsNullOrEmpty(this.sessionUUID.Trim()))
			{
				this.sessionUUID = Guid.NewGuid().ToString();
			}
		}
		
		private int GetTimeoutInSecondsForResponseType(ResponseType type)
		{
			int timeout;
			if (type == ResponseType.Subscribe || type == ResponseType.Presence)
			{
				timeout = _pubnubWebRequestCallbackIntervalInSeconds;
			}
			else
			{
				timeout = _pubnubOperationTimeoutIntervalInSeconds;
			}
			return timeout;
		}
		
		private void TerminateHeartbeatTimer()
		{
			TerminateHeartbeatTimer(null);
		}
		
		private void TerminateHeartbeatTimer(Uri requestUri)
		{
			if (requestUri != null)
			{
				if (_channelHeartbeatTimer.ContainsKey(requestUri))
				{
					Timer requestHeatbeatTimer = _channelHeartbeatTimer[requestUri];
					if (requestHeatbeatTimer != null)
					{
						try
						{
							requestHeatbeatTimer.Change(
								(-1 == _pubnubNetworkTcpCheckIntervalInSeconds) ? -1 : _pubnubNetworkTcpCheckIntervalInSeconds * 1000,
								(-1 == _pubnubNetworkTcpCheckIntervalInSeconds) ? -1 : _pubnubNetworkTcpCheckIntervalInSeconds * 1000);
							requestHeatbeatTimer.Dispose();
						}
						catch (ObjectDisposedException ex)
						{
							LoggingMethod.WriteToLog(string.Format("DateTime {0} Error while accessing requestHeatbeatTimer object in TerminateHeartbeatTimer {1}", DateTime.Now.ToString(), ex.ToString()), LoggingMethod.LevelInfo);
						}
						
						Timer removedTimer = null;
						bool removed = _channelHeartbeatTimer.TryRemove(requestUri, out removedTimer);
						if (removed)
						{
							LoggingMethod.WriteToLog(string.Format("DateTime {0} Remove heartbeat reference from collection for {1}", DateTime.Now.ToString(), requestUri.ToString()), LoggingMethod.LevelInfo);
						}
						else
						{
							LoggingMethod.WriteToLog(string.Format("DateTime {0} Unable to remove heartbeat reference from collection for {1}", DateTime.Now.ToString(), requestUri.ToString()), LoggingMethod.LevelInfo);
						}
					}
				}
			}
			else
			{
				ConcurrentDictionary<Uri, Timer> timerCollection = _channelHeartbeatTimer;
				ICollection<Uri> keyCollection = timerCollection.Keys;
				foreach (Uri key in keyCollection)
				{
					if (_channelHeartbeatTimer.ContainsKey(key))
					{
						Timer currentTimer = _channelHeartbeatTimer[key];
						currentTimer.Dispose();
						Timer removedTimer = null;
						bool removed = _channelHeartbeatTimer.TryRemove(key, out removedTimer);
						if (!removed)
						{
							LoggingMethod.WriteToLog(string.Format("DateTime {0} TerminateHeartbeatTimer(null) - Unable to remove heartbeat reference from collection for {1}", DateTime.Now.ToString(), key.ToString()), LoggingMethod.LevelInfo);
						}
					}
				}
			}
		}
		
		private void TerminateReconnectTimer()
		{
			TerminateReconnectTimer(null);
		}
		
		private void TerminateReconnectTimer(string channelName)
		{
			if (channelName != null)
			{
				if (_channelReconnectTimer.ContainsKey(channelName))
				{
					Timer channelReconnectTimer = _channelReconnectTimer[channelName];
					channelReconnectTimer.Change(
						(-1 == _pubnubNetworkTcpCheckIntervalInSeconds) ? -1 : _pubnubNetworkTcpCheckIntervalInSeconds * 1000,
						(-1 == _pubnubNetworkTcpCheckIntervalInSeconds) ? -1 : _pubnubNetworkTcpCheckIntervalInSeconds * 1000);
					channelReconnectTimer.Dispose();
					Timer removedTimer = null;
					bool removed = _channelReconnectTimer.TryRemove(channelName, out removedTimer);
					if (!removed)
					{
						LoggingMethod.WriteToLog(string.Format("DateTime {0} TerminateReconnectTimer - Unable to remove reconnect timer reference from collection for {1}", DateTime.Now.ToString(), channelName), LoggingMethod.LevelInfo);
					}
				}
			}
			else
			{
				ConcurrentDictionary<string, Timer> reconnectCollection = _channelReconnectTimer;
				ICollection<string> keyCollection = reconnectCollection.Keys;
				foreach (string key in keyCollection)
				{
					if (_channelReconnectTimer.ContainsKey(key))
					{
						Timer currentTimer = _channelReconnectTimer[key];
						currentTimer.Dispose();
						Timer removedTimer = null;
						bool removed = _channelReconnectTimer.TryRemove(key, out removedTimer);
						if (!removed)
						{
							LoggingMethod.WriteToLog(string.Format("DateTime {0} TerminateReconnectTimer(null) - Unable to remove reconnect timer reference from collection for {1}", DateTime.Now.ToString(), key.ToString()), LoggingMethod.LevelInfo);
						}
					}
				}
			}
		}
		
		private void ResponseToConnectCallback<T>(List<object> result, ResponseType type, string[] channels, Action<T> connectCallback)
		{
			//Check callback exists and make sure previous timetoken = 0
			if (channels != null && connectCallback != null
			    && channels.Length > 0)
			{
				IEnumerable<string> newChannels = from channel in _multiChannelSubscribe
					where channel.Value == 0
						select channel.Key;
				foreach (string channel in newChannels)
				{
					string jsonString = "";
					List<object> connectResult = new List<object>();
					switch (type)
					{
					case ResponseType.Subscribe:
						jsonString = string.Format("[1, \"Connected\"]");
						connectResult = _jsonPluggableLibrary.DeserializeToListOfObject(jsonString);
						connectResult.Add(channel);

                        if (_channelCallbacks.Count > 0 && _channelCallbacks.ContainsKey(channel))
                        {
                            PubnubChannelCallback<T> currentPubnubCallback = _channelCallbacks[channel] as PubnubChannelCallback<T>;
                            if (currentPubnubCallback != null && currentPubnubCallback.ConnectCallback != null)
                            {
                                GoToCallback<T>(connectResult, currentPubnubCallback.ConnectCallback);
                            }
                        }
						break;
					case ResponseType.Presence:
						jsonString = string.Format("[1, \"Presence Connected\"]");
						connectResult = _jsonPluggableLibrary.DeserializeToListOfObject(jsonString);
						connectResult.Add(channel.Replace("-pnpres", ""));

                        if (_channelCallbacks.Count > 0 && _channelCallbacks.ContainsKey(channel))
                        {
                            PubnubChannelCallback<T> currentPubnubCallback = _channelCallbacks[channel] as PubnubChannelCallback<T>;
                            if (currentPubnubCallback != null && currentPubnubCallback.ConnectCallback != null)
                            {
                                GoToCallback<T>(connectResult, currentPubnubCallback.ConnectCallback);
                            }
                        }
						break;
					default:
						break;
					}
				}
			}
			
		}
		
		public Uri BuildTimeRequest()
		{
			List<string> url = new List<string>();
			
			url.Add("time");
			url.Add("0");
			
			return BuildRestApiRequest<Uri>(url, ResponseType.Time);
			
		}
		
		private Uri BuildMultiChannelLeaveRequest(string[] channels)
		{
			List<string> url = new List<string>();
			
			url.Add("v2");
			url.Add("presence");
			url.Add("sub_key");
			url.Add(this.subscribeKey);
			url.Add("channel");
			url.Add(string.Join(",", channels));
			url.Add("leave");
			
			return BuildRestApiRequest<Uri>(url, ResponseType.Leave);
		}
		
		private Uri BuildHereNowRequest(string channel)
		{
			List<string> url = new List<string>();
			
			url.Add("v2");
			url.Add("presence");
			url.Add("sub_key");
			url.Add(this.subscribeKey);
			url.Add("channel");
			url.Add(channel);
			
			return BuildRestApiRequest<Uri>(url, ResponseType.Here_Now);
		}
		
		private Uri BuildDetailedHistoryRequest(string channel, long start, long end, int count, bool reverse)
		{
			parameters = "";
			if (count <= -1) count = 100;
			parameters = "?count=" + count;
			if (reverse)
				parameters = parameters + "&" + "reverse=" + reverse.ToString().ToLower();
			if (start != -1)
				parameters = parameters + "&" + "start=" + start.ToString().ToLower();
			if (end != -1)
				parameters = parameters + "&" + "end=" + end.ToString().ToLower();
            if (!string.IsNullOrEmpty(_authenticationKey))
            {
                parameters = parameters + "&" + "auth=" + _authenticationKey;
            }
			
			List<string> url = new List<string>();
			
			url.Add("v2");
			url.Add("history");
			url.Add("sub-key");
			url.Add(this.subscribeKey);
			url.Add("channel");
			url.Add(channel);
			
			return BuildRestApiRequest<Uri>(url, ResponseType.DetailedHistory);
		}
		
		private Uri BuildMultiChannelSubscribeRequest(string[] channels, object timetoken)
		{
			List<string> url = new List<string>();
			url.Add("subscribe");
			url.Add(this.subscribeKey);
			url.Add(string.Join(",", channels));
			url.Add("0");
			url.Add(timetoken.ToString());
			
			return BuildRestApiRequest<Uri>(url, ResponseType.Subscribe);
		}

        private Uri BuildGrantAccessRequest(string channel, bool read, bool write, int ttl)
        {
            string signature = "0";
            long timeStamp = TranslateDateTimeToSeconds(DateTime.UtcNow);
            string queryString = "";
            StringBuilder queryStringBuilder = new StringBuilder();
            if (!string.IsNullOrEmpty(_authenticationKey))
            {
                queryStringBuilder.AppendFormat("auth={0}", EncodeUricomponent(_authenticationKey, ResponseType.GrantAccess, false));
            }

            //if (!string.IsNullOrEmpty(channel) && !string.IsNullOrEmpty(channel.Replace("-pnpres", "")))
            if (!string.IsNullOrEmpty(channel))
            {
                queryStringBuilder.AppendFormat("{0}channel={1}", (queryStringBuilder.Length > 0) ? "&" : "", EncodeUricomponent(channel, ResponseType.GrantAccess, false));
            }

            queryStringBuilder.AppendFormat("{0}", (queryStringBuilder.Length > 0) ? "&" : "");
            queryStringBuilder.AppendFormat("r={0}&timestamp={1}{2}&w={3}", Convert.ToInt32(read), timeStamp.ToString(), (ttl > -1) ? "&ttl=" + ttl.ToString() : "", Convert.ToInt32(write));
            
            if (this.secretKey.Length > 0)
            {
                StringBuilder string_to_sign = new StringBuilder();
                string_to_sign.Append(this.subscribeKey)
                    .Append("\n")
                    .Append(this.publishKey)
                    .Append("\n")
                    .Append("grant")
                    .Append("\n")
                    .Append(queryStringBuilder.ToString());

                PubnubCrypto pubnubCrypto = new PubnubCrypto(this.cipherKey);
                signature = pubnubCrypto.PubnubAccessManagerSign(this.secretKey, string_to_sign.ToString());
                queryString = string.Format("signature={0}&{1}", signature, queryStringBuilder.ToString());
            }

            parameters = "";
            parameters += "?" + queryString;

            List<string> url = new List<string>();
            url.Add("v1");
            url.Add("auth");
            url.Add("grant");
            url.Add("sub-key");
            url.Add(this.subscribeKey);

            return BuildRestApiRequest<Uri>(url, ResponseType.GrantAccess);
        }

        private Uri BuildAuditAccessRequest(string channel)
        {
            string signature = "0";
            long timeStamp = ((_pubnubUnitTest == null) || (_pubnubUnitTest is IPubnubUnitTest && !_pubnubUnitTest.EnableStubTest))
                                    ? TranslateDateTimeToSeconds(DateTime.UtcNow) 
                                    : TranslateDateTimeToSeconds(new DateTime(2013,01,01));
            string queryString="";
            StringBuilder queryStringBuilder = new StringBuilder();
            if (!string.IsNullOrEmpty(_authenticationKey))
            {
                queryStringBuilder.AppendFormat("auth={0}", EncodeUricomponent(_authenticationKey, ResponseType.AuditAccess, false));
            }
            //if (authLimit > 0)
            //{
            //    queryStringBuilder.AppendFormat("{0}auth_limit={1}", (queryStringBuilder.Length > 0) ? "&" : "", authLimit);
            //}
            if (!string.IsNullOrEmpty(channel))
            {
                queryStringBuilder.AppendFormat("{0}channel={1}", (queryStringBuilder.Length > 0) ? "&" : "", EncodeUricomponent(channel, ResponseType.AuditAccess, false));
            }
            //if (channelLimit > 0)
            //{
            //    queryStringBuilder.AppendFormat("{0}channel_limit={1}", (queryStringBuilder.Length > 0) ? "&" : "", channelLimit);
            //}
            queryStringBuilder.AppendFormat("{0}timestamp={1}", (queryStringBuilder.Length > 0) ? "&" : "", timeStamp.ToString());

            if (this.secretKey.Length > 0)
            {
                StringBuilder string_to_sign = new StringBuilder();
                string_to_sign.Append(this.subscribeKey)
                    .Append("\n")
                    .Append(this.publishKey)
                    .Append("\n")
                    .Append("audit")
                    .Append("\n")
                    .Append(queryStringBuilder.ToString());

                PubnubCrypto pubnubCrypto = new PubnubCrypto(this.cipherKey);
                signature = pubnubCrypto.PubnubAccessManagerSign(this.secretKey, string_to_sign.ToString());
                queryString = string.Format("signature={0}&{1}", signature, queryStringBuilder.ToString());
            }

            parameters = "";
            parameters += "?" + queryString;

            List<string> url = new List<string>();
            url.Add("v1");
            url.Add("auth");
            url.Add("audit");
            url.Add("sub-key");
            url.Add(this.subscribeKey);

            return BuildRestApiRequest<Uri>(url, ResponseType.AuditAccess);
        }

		private Uri BuildPublishRequest(string channel, object originalMessage)
		{
			string message = (_enableJsonEncodingForPublish) ? JsonEncodePublishMsg(originalMessage) : originalMessage.ToString();
			
			// Generate String to Sign
			string signature = "0";
			if (this.secretKey.Length > 0)
			{
				StringBuilder string_to_sign = new StringBuilder();
				string_to_sign
					.Append(this.publishKey)
						.Append('/')
						.Append(this.subscribeKey)
						.Append('/')
						.Append(this.secretKey)
						.Append('/')
						.Append(channel)
						.Append('/')
						.Append(message); // 1
				
				// Sign Message
				signature = Md5(string_to_sign.ToString());
			}
			
			// Build URL
			List<string> url = new List<string>();
			url.Add("publish");
			url.Add(this.publishKey);
			url.Add(this.subscribeKey);
			url.Add(signature);
			url.Add(channel);
			url.Add("0");
			url.Add(message);
			
			return BuildRestApiRequest<Uri>(url, ResponseType.Publish);
		}
		
		private Uri BuildRestApiRequest<T>(List<string> urlComponents, ResponseType type)
		{
			StringBuilder url = new StringBuilder();
			
			// Add Origin To The Request
			url.Append(this._origin);
			
			// Generate URL with UTF-8 Encoding
			for (int componentIndex = 0; componentIndex < urlComponents.Count; componentIndex++)
			{
				url.Append("/");
				
				if (type == ResponseType.Publish && componentIndex == urlComponents.Count - 1)
				{
					url.Append(EncodeUricomponent(urlComponents[componentIndex].ToString(), type, false));
				}
				else
				{
					url.Append(EncodeUricomponent(urlComponents[componentIndex].ToString(), type, true));
				}
			}
			
			VerifyOrSetSessionUUID();
			if (type == ResponseType.Presence || type == ResponseType.Subscribe || type == ResponseType.Leave)
			{
				url.AppendFormat("?uuid={0}",this.sessionUUID);
                if (!string.IsNullOrEmpty(_authenticationKey))
                {
                    url.AppendFormat("&auth={0}", EncodeUricomponent(_authenticationKey, type, false));
                }
			}

            if ((type == ResponseType.Here_Now || type == ResponseType.Publish) && (!string.IsNullOrEmpty(_authenticationKey)))
            {
                url.AppendFormat("?auth={0}", EncodeUricomponent(_authenticationKey, type, false));
            }
			
			if (type == ResponseType.DetailedHistory || type == ResponseType.GrantAccess || type == ResponseType.AuditAccess || type == ResponseType.RevokeAccess)
				url.Append(parameters);
			
#if (WINDOWS_PHONE)
				url.Append("&nocache=");
				url.Append(Guid.NewGuid().ToString());
#endif

            Uri requestUri = new Uri(url.ToString());
			
#if ((!__MonoCS__) && (!SILVERLIGHT) && (!WINDOWS_PHONE) && (!UNITY_STANDALONE) && (!UNITY_WEBPLAYER) && (!UNITY_IOS) && (!UNITY_ANDROID))
			if ((type == ResponseType.Publish || type == ResponseType.Subscribe || type == ResponseType.Presence))
			{
				// Force canonical path and query
				string paq = requestUri.PathAndQuery;
				FieldInfo flagsFieldInfo = typeof(Uri).GetField("m_Flags", BindingFlags.Instance | BindingFlags.NonPublic);
				ulong flags = (ulong)flagsFieldInfo.GetValue(requestUri);
				flags &= ~((ulong)0x30); // Flags.PathNotCanonical|Flags.QueryNotCanonical
				flagsFieldInfo.SetValue(requestUri, flags);
			}
#endif
			
			return requestUri;
			
		}
		
		void OnPubnubWebRequestTimeout<T>(System.Object requestState)
		{
			RequestState<T> currentState = requestState as RequestState<T>;
			if (currentState != null && currentState.Response == null && currentState.Request != null)
			{
				currentState.Timeout = true;
				currentState.Request.Abort();
				LoggingMethod.WriteToLog(string.Format("DateTime: {0}, **WP7 OnPubnubWebRequestTimeout**", DateTime.Now.ToString()), LoggingMethod.LevelError);
			}
		}
		
		private void UrlRequestCommonExceptionHandler<T>(ResponseType type, string[] channels, bool requestTimeout, Action<T> userCallback, Action<T> connectCallback, Action<PubnubClientError> errorCallback, bool resumeOnReconnect)
		{
			if (type == ResponseType.Subscribe || type == ResponseType.Presence)
			{
				MultiplexExceptionHandler<T>(type, channels, userCallback, connectCallback, errorCallback, false, resumeOnReconnect);
			}
			else if (type == ResponseType.Publish)
			{
				PublishExceptionHandler<T>(channels[0], requestTimeout, errorCallback);
			}
			else if (type == ResponseType.Here_Now)
			{
                HereNowExceptionHandler<T>(channels[0], requestTimeout, errorCallback);
			}
			else if (type == ResponseType.DetailedHistory)
			{
                DetailedHistoryExceptionHandler<T>(channels[0], requestTimeout, errorCallback);
			}
			else if (type == ResponseType.Time)
			{
                TimeExceptionHandler<T>(requestTimeout, errorCallback);
			}
			else if (type == ResponseType.Leave)
			{
				//no action at this time
			}
		}
		
		private void ResponseToUserCallback<T>(List<object> result, ResponseType type, string[] channels, Action<T> userCallback)
		{
			string[] messageChannels;
			switch (type)
			{
			case ResponseType.Subscribe:
			case ResponseType.Presence:
				var messages = (from item in result
				                select item as object).ToArray();
				if (messages != null && messages.Length > 0)
				{
					object[] messageList = messages[0] as object[];
					messageChannels = messages[2].ToString().Split(',');
					
					if (messageList != null && messageList.Length > 0)
					{
						for (int messageIndex = 0; messageIndex < messageList.Length; messageIndex++)
						{
							string currentChannel = (messageChannels.Length == 1) ? (string)messageChannels[0] : (string)messageChannels[messageIndex];
							List<object> itemMessage = new List<object>();
							if (currentChannel.Contains("-pnpres"))
							{
								itemMessage.Add(messageList[messageIndex]);
							}
							else
							{
								//decrypt the subscriber message if cipherkey is available
								if (this.cipherKey.Length > 0)
								{
									PubnubCrypto aes = new PubnubCrypto(this.cipherKey);
									string decryptMessage = aes.Decrypt(messageList[messageIndex].ToString());
									object decodeMessage = (decryptMessage == "**DECRYPT ERROR**") ? decryptMessage : _jsonPluggableLibrary.DeserializeToObject(decryptMessage);
									
									itemMessage.Add(decodeMessage);
								}
								else
								{
									itemMessage.Add(messageList[messageIndex]);
								}
							}
							itemMessage.Add(messages[1].ToString());
                            itemMessage.Add(currentChannel.Replace("-pnpres", ""));
                            if (_channelCallbacks.Count > 0 && _channelCallbacks.ContainsKey(currentChannel))
                            {
                                PubnubChannelCallback<T> currentPubnubCallback = _channelCallbacks[currentChannel] as PubnubChannelCallback<T>;
                                if (currentPubnubCallback != null && currentPubnubCallback.Callback != null)
                                {
                                    GoToCallback<T>(itemMessage, currentPubnubCallback.Callback);
                                }
                            }
						}
					}
				}
				break;
			case ResponseType.Publish:
				if (result != null && result.Count > 0)
				{
					GoToCallback<T>(result, userCallback);
				}
				break;
			case ResponseType.DetailedHistory:
				if (result != null && result.Count > 0)
				{
					GoToCallback<T>(result, userCallback);
				}
				break;
			case ResponseType.Here_Now:
				if (result != null && result.Count > 0)
				{
					GoToCallback<T>(result, userCallback);
				}
				break;
			case ResponseType.Time:
				if (result != null && result.Count > 0)
				{
					GoToCallback<T>(result, userCallback);
				}
				break;
			case ResponseType.Leave:
				//No response to callback
				break;
            case ResponseType.GrantAccess:
            case ResponseType.AuditAccess:
            case ResponseType.RevokeAccess:
                if (result != null && result.Count > 0)
                {
                    GoToCallback<T>(result, userCallback);
                }
                break;
			default:
				break;
			}
		}
		
		private void JsonResponseToCallback<T>(List<object> result, Action<T> callback)
		{
			string callbackJson = "";
			
			if (typeof(T) == typeof(string))
			{
				callbackJson = _jsonPluggableLibrary.SerializeToJsonString(result);
				
				Action<string> castCallback = callback as Action<string>;
				castCallback(callbackJson);
			}
		}

        private void JsonResponseToCallback<T>(object result, Action<T> callback)
        {
            string callbackJson = "";

            if (typeof(T) == typeof(string))
            {
                callbackJson = _jsonPluggableLibrary.SerializeToJsonString(result);

                Action<string> castCallback = callback as Action<string>;
                castCallback(callbackJson);
            }
        }
		
		private void MultiplexExceptionHandler<T>(ResponseType type, string[] channels, Action<T> userCallback, Action<T> connectCallback, Action<PubnubClientError> errorCallback, bool reconnectMaxTried, bool resumeOnReconnect)
		{
			string channel = "";
			if (channels != null)
			{
				channel = string.Join(",", channels);
			}
			
			if (reconnectMaxTried)
			{
				LoggingMethod.WriteToLog(string.Format("DateTime {0}, MAX retries reached. Exiting the subscribe for channel(s) = {1}", DateTime.Now.ToString(), channel), LoggingMethod.LevelInfo);
				
				string[] activeChannels = _multiChannelSubscribe.Keys.ToArray<string>();
				MultiChannelUnSubscribeInit<T>(ResponseType.Unsubscribe, string.Join(",", activeChannels), null, null, null, null);
				
				string[] subscribeChannels = activeChannels.Where(filterChannel => !filterChannel.Contains("-pnpres")).ToArray();
				string[] presenceChannels = activeChannels.Where(filterChannel => filterChannel.Contains("-pnpres")).ToArray();
				
				if (subscribeChannels != null && subscribeChannels.Length > 0)
				{
                    for (int index = 0; index < subscribeChannels.Length; index++)
                    {
                        //List<object> errorResult = new List<object>();
                        string message = string.Format("Unsubscribed after {0} failed retries", _pubnubNetworkCheckRetries);
                        //errorResult = _jsonPluggableLibrary.DeserializeToListOfObject(jsonString);
                        string activeChannel = subscribeChannels[index].ToString() ;//string.Join(",", subscribeChannels);
                        //errorResult.Add(activeChannel);

                        if (_channelCallbacks.Count > 0 && _channelCallbacks.ContainsKey(activeChannel))
                        {
                            PubnubChannelCallback<T> currentPubnubCallback = _channelCallbacks[activeChannel] as PubnubChannelCallback<T>;
                            if (currentPubnubCallback != null && currentPubnubCallback.Callback != null)
                            {
                                PubnubClientError error = new PubnubClientError(0, PubnubErrorSeverity.Info, message, PubnubMessageSource.Client, activeChannel);
                                GoToCallback(error, currentPubnubCallback.ErrorCallback);
                            }
                        }

                        LoggingMethod.WriteToLog(string.Format("DateTime {0}, Subscribe JSON network error response={1}", DateTime.Now.ToString(), message), LoggingMethod.LevelInfo);
                    }

				}
				if (presenceChannels != null && presenceChannels.Length > 0)
				{
                    for (int index = 0; index < presenceChannels.Length; index++)
                    {
                        //List<object> errorResult = new List<object>();
                        string message = string.Format("Presence Unsubscribed after {0} failed retries", _pubnubNetworkCheckRetries);
                        //errorResult = _jsonPluggableLibrary.DeserializeToListOfObject(jsonString);
                        string activeChannel = presenceChannels[index].ToString();
                        //errorResult.Add(activeChannel.Replace("-pnpres", ""));

                        if (_channelCallbacks.Count > 0 && _channelCallbacks.ContainsKey(activeChannel))
                        {
                            PubnubChannelCallback<T> currentPubnubCallback = _channelCallbacks[activeChannel] as PubnubChannelCallback<T>;
                            if (currentPubnubCallback != null && currentPubnubCallback.Callback != null)
                            {
                                PubnubClientError error = new PubnubClientError(0, PubnubErrorSeverity.Info, message, PubnubMessageSource.Client, activeChannel);
                                GoToCallback(error, currentPubnubCallback.ErrorCallback);
                            }
                        }

                        LoggingMethod.WriteToLog(string.Format("DateTime {0}, Presence-Subscribe JSON network error response={1}", DateTime.Now.ToString(), message), LoggingMethod.LevelInfo);
                    }
				}
				
			}
			else
			{
				List<object> result = new List<object>();
				result.Add("0");
				if (resumeOnReconnect)
				{
					result.Add(0); //send 0 time token to enable presence event
				}
				else
				{
					result.Add(lastSubscribeTimetoken); //get last timetoken
				}
				result.Add(channels); //send channel name
				
				MultiplexInternalCallback<T>(type, result, userCallback, connectCallback, errorCallback);
			}
		}
		
		private void PublishExceptionHandler<T>(string channelName, bool requestTimeout, Action<PubnubClientError> errorCallback)
		{
			//List<object> result = new List<object>();
            if (requestTimeout)
            {
                string message = (requestTimeout) ? "Operation Timeout" : "Network connnect error";
                //result = _jsonPluggableLibrary.DeserializeToListOfObject(jsonString);
                //result.Add(channelName);
                LoggingMethod.WriteToLog(string.Format("DateTime {0}, JSON publish response={1}", DateTime.Now.ToString(), message), LoggingMethod.LevelInfo);
                PubnubClientError error = new PubnubClientError(0, PubnubErrorSeverity.Info, message, PubnubMessageSource.Client, channelName);
                GoToCallback(error, errorCallback);
            }
		}
		
		private void HereNowExceptionHandler<T>(string channelName, bool requestTimeout, Action<PubnubClientError> errorCallback)
		{
			//List<object> result = new List<object>();
            if (requestTimeout)
            {
                string message = (requestTimeout) ? "Operation Timeout" : "Network connnect error";
                //result = _jsonPluggableLibrary.DeserializeToListOfObject(message);
                //result.Add(channelName);
                LoggingMethod.WriteToLog(string.Format("DateTime {0}, HereNowExceptionHandler response={1}", DateTime.Now.ToString(), message), LoggingMethod.LevelInfo);
                PubnubClientError error = new PubnubClientError(0, PubnubErrorSeverity.Info, message, PubnubMessageSource.Client, channelName);
                GoToCallback(error, errorCallback);
            }
		}
		
		private void DetailedHistoryExceptionHandler<T>(string channelName, bool requestTimeout, Action<PubnubClientError> errorCallback)
		{
			//List<object> result = new List<object>();
            if (requestTimeout)
            {
                string message = (requestTimeout) ? "Operation Timeout" : "Network connnect error";
                //result = _jsonPluggableLibrary.DeserializeToListOfObject(jsonString);
                //result.Add(channelName);
                LoggingMethod.WriteToLog(string.Format("DateTime {0}, DetailedHistoryExceptionHandler response={1}", DateTime.Now.ToString(), message), LoggingMethod.LevelInfo);
                PubnubClientError error = new PubnubClientError(0, PubnubErrorSeverity.Info, message, PubnubMessageSource.Client, channelName);
                GoToCallback(error, errorCallback);
            }
		}
		
		private void TimeExceptionHandler<T>(bool requestTimeout, Action<PubnubClientError> errorCallback)
		{
			//List<object> result = new List<object>();
            if (requestTimeout)
            {
                string message = (requestTimeout) ? "Operation Timeout" : "Network connnect error";
                //result = _jsonPluggableLibrary.DeserializeToListOfObject(jsonString);
                LoggingMethod.WriteToLog(string.Format("DateTime {0}, TimeExceptionHandler response={1}", DateTime.Now.ToString(), message), LoggingMethod.LevelInfo);
                PubnubClientError error = new PubnubClientError(0, PubnubErrorSeverity.Info, message, PubnubMessageSource.Client, "");
                GoToCallback(error, errorCallback);
            }
		}
		
		/// <summary>
		/// Gets the result by wrapping the json response based on the request
		/// </summary>
		/// <typeparam name="T"></typeparam>
		/// <param name="type"></param>
		/// <param name="jsonString"></param>
		/// <param name="channels"></param>
		/// <param name="reconnect"></param>
		/// <param name="lastTimetoken"></param>
		/// <param name="errorCallback"></param>
		/// <returns></returns>
		private List<object> WrapResultBasedOnResponseType<T>(ResponseType type, string jsonString, string[] channels, bool reconnect, long lastTimetoken, Action<PubnubClientError> errorCallback)
		{
			List<object> result = new List<object>();
			
			try
			{
                string multiChannel = (channels != null) ? string.Join(",", channels) : "";
                if (!string.IsNullOrEmpty(jsonString))
                {
                    if (!string.IsNullOrEmpty(jsonString))
                    {
                        object deSerializedResult = _jsonPluggableLibrary.DeserializeToObject(jsonString);
                        List<object> result1 = ((IEnumerable)deSerializedResult).Cast<object>().ToList();

                        if (result1 != null && result1.Count > 0)
                        {
                            result = result1;
                        }

                        switch (type)
                        {
                            case ResponseType.Publish:
                                result.Add(multiChannel);
                                break;
                            case ResponseType.History:
                                if (this.cipherKey.Length > 0)
                                {
                                    List<object> historyDecrypted = new List<object>();
                                    PubnubCrypto aes = new PubnubCrypto(this.cipherKey);
                                    foreach (object message in result)
                                    {
                                        historyDecrypted.Add(aes.Decrypt(message.ToString()));
                                    }
                                    History = historyDecrypted;
                                }
                                else
                                {
                                    History = result;
                                }
                                break;
                            case ResponseType.DetailedHistory:
                                result = DecodeDecryptLoop(result, channels, errorCallback);
                                result.Add(multiChannel);
                                break;
                            case ResponseType.Here_Now:
                                Dictionary<string, object> dictionary = _jsonPluggableLibrary.DeserializeToDictionaryOfObject(jsonString);
                                result = new List<object>();
                                result.Add(dictionary);
                                result.Add(multiChannel);
                                break;
                            case ResponseType.Time:
                                break;
                            case ResponseType.Subscribe:
                            case ResponseType.Presence:
                                result.Add(multiChannel);
                                long receivedTimetoken = (result.Count > 1) ? Convert.ToInt64(result[1].ToString()) : 0;
                                long minimumTimetoken = (_multiChannelSubscribe.Count > 0) ? _multiChannelSubscribe.Min(token => token.Value) : 0;
                                long maximumTimetoken = (_multiChannelSubscribe.Count > 0) ? _multiChannelSubscribe.Max(token => token.Value) : 0;

                                if (minimumTimetoken == 0 || lastTimetoken == 0)
                                {
                                    if (maximumTimetoken == 0)
                                    {
                                        lastSubscribeTimetoken = receivedTimetoken;
                                    }
                                    else
                                    {
                                        if (!_enableResumeOnReconnect)
                                        {
                                            lastSubscribeTimetoken = receivedTimetoken;
                                        }
                                        else
                                        {
                                            //do nothing. keep last subscribe token
                                        }
                                    }
                                }
                                else
                                {
                                    if (reconnect)
                                    {
                                        if (_enableResumeOnReconnect)
                                        {
                                            //do nothing. keep last subscribe token
                                        }
                                        else
                                        {
                                            lastSubscribeTimetoken = receivedTimetoken;
                                        }
                                    }
                                    else
                                    {
                                        lastSubscribeTimetoken = receivedTimetoken;
                                    }
                                }
                                break;
                            case ResponseType.Leave:
                                result.Add(multiChannel);
                                break;
                            case ResponseType.GrantAccess:
                            case ResponseType.AuditAccess:
                            case ResponseType.RevokeAccess:
                                Dictionary<string, object> grantDictionary = _jsonPluggableLibrary.DeserializeToDictionaryOfObject(jsonString);
                                result = new List<object>();
                                result.Add(grantDictionary);
                                result.Add(multiChannel);
                                break;
                            default:
                                break;
                        };//switch stmt end
                    }
                }
			}
			catch (Exception ex)
			{
                PubnubErrorCode errorType = PubnubErrorCodeHelper.GetErrorType(ex);
                int statusCode = (int)errorType;
                string errorDescription = PubnubErrorCodeDescription.GetStatusCodeDescription(errorType);
                if (channels != null)
                {
                    if (type == ResponseType.Subscribe
                        || type == ResponseType.Presence)
                    {
                        for (int index = 0; index < channels.Length; index++)
                        {
                            string activeChannel = channels[index].ToString();
                            if (_channelCallbacks.Count > 0 && _channelCallbacks.ContainsKey(activeChannel))
                            {
                                PubnubChannelCallback<T> currentPubnubCallback = _channelCallbacks[activeChannel] as PubnubChannelCallback<T>;
                                if (currentPubnubCallback != null && currentPubnubCallback.ErrorCallback != null)
                                {
                                    PubnubClientError error = new PubnubClientError(statusCode, PubnubErrorSeverity.Warn, true, ex.Message, ex, PubnubMessageSource.Client, null, null, errorDescription, activeChannel);
                                    GoToCallback(error, currentPubnubCallback.ErrorCallback);
                                }
                            }
                        }
                    }
                    else
                    {
                        if (errorCallback != null)
                        {
                            PubnubClientError error = new PubnubClientError(statusCode, PubnubErrorSeverity.Warn, true, ex.Message, ex, PubnubMessageSource.Client, null, null, errorDescription, string.Join(",", channels));
                            GoToCallback(error, errorCallback);
                        }
                    }
                }
			}
			return result;
		}
		
        private void GoToCallback<T>(object result, Action<T> Callback)
        {
            if (Callback != null)
            {
                if (typeof(T) == typeof(string))
                {
                    JsonResponseToCallback(result, Callback);
                }
                else
                {
                    Callback((T)(object)result);
                }
            }
        }

        private void GoToCallback(PubnubClientError error, Action<PubnubClientError> Callback)
        {
            if (Callback != null && error != null)
            {
                if (error.StatusCode != 107 && error.StatusCode != 105)
                {
                    //TODO:
                    //Switch statement to include custom message related to error/status code for developer
                    //switch (error.StatusCode)
                    //{
                    //    case 1:
                    //        //error.AdditionalMessage = MyCustomErrorMessageRetrieverBasedOnStatusCode(error.StatusCode);
                    //        break;
                    //    case 112:
                    //        error.AdditionalMessage = "Bad Auth Key";
                    //        break;
                    //    case 113:
                    //        error.AdditionalMessage = "Need to set Auth Key";
                    //        break;
                    //    default:
                    //        break;
                    //}
                    //Console.WriteLine(error.ToString());
                    Callback(error);
                }
            }
        }

		/// <summary>
		/// Retrieves the channel name from the url components
		/// </summary>
		/// <param name="urlComponents"></param>
		/// <param name="type"></param>
		/// <returns></returns>
		private string GetChannelName(List<string> urlComponents, ResponseType type)
		{
			string channelName = "";
			switch (type)
			{
			case ResponseType.Subscribe:
			case ResponseType.Presence:
				channelName = urlComponents[2];
				break;
			case ResponseType.Publish:
				channelName = urlComponents[4];
				break;
			case ResponseType.DetailedHistory:
				channelName = urlComponents[5];
				break;
			case ResponseType.Here_Now:
				channelName = urlComponents[5];
				break;
			case ResponseType.Leave:
				channelName = urlComponents[5];
				break;
			default:
				break;
			};
			return channelName;
		}
		
		private string EncodeUricomponent(string s, ResponseType type, bool ignoreComma)
		{
			string encodedUri = "";
			StringBuilder o = new StringBuilder();
			foreach (char ch in s)
			{
				if (IsUnsafe(ch,ignoreComma))
				{
					o.Append('%');
					o.Append(ToHex(ch / 16));
					o.Append(ToHex(ch % 16));
				}
                else
                {
                    if (ch == ',' && ignoreComma)
                    {
                        o.Append(ch.ToString());
                    }
                    else
                    {
                        string escapeChar = System.Uri.EscapeDataString(ch.ToString());
                        o.Append(escapeChar);
                    }
                }
			}
			encodedUri = o.ToString();
			if (type == ResponseType.Here_Now || type == ResponseType.DetailedHistory || type == ResponseType.Leave)
			{
				encodedUri = encodedUri.Replace("%2F", "%252F");
			}
			
			return encodedUri;
		}
		
		private char ToHex(int ch)
		{
			return (char)(ch < 10 ? '0' + ch : 'A' + ch - 10);
		}
		
		private bool IsUnsafe(char ch, bool ignoreComma)
		{
			if (ignoreComma)
			{
				return " ~`!@#$%^&*()+=[]\\{}|;':\"/<>?".IndexOf(ch) >= 0;
			}
			else
			{
				return " ~`!@#$%^&*()+=[]\\{}|;':\",/<>?".IndexOf(ch) >= 0;
			}
		}
		
		
		public Guid GenerateGuid()
		{
			return Guid.NewGuid();
		}
		
		private static string Md5(string text)
		{
			MD5 md5 = new MD5CryptoServiceProvider();
			byte[] data = Encoding.Unicode.GetBytes(text);
			byte[] hash = md5.ComputeHash(data);
			string hexaHash = "";
			foreach (byte b in hash) hexaHash += String.Format("{0:x2}", b);
			return hexaHash;
		}


        public static long TranslateDateTimeToSeconds(DateTime dotNetUTCDateTime)
        {
            TimeSpan timeSpan = dotNetUTCDateTime - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            long timeStamp = Convert.ToInt64(timeSpan.TotalSeconds);
            return timeStamp;
        }

        /// <summary>
		/// Convert the UTC/GMT DateTime to Unix Nano Seconds format
		/// </summary>
		/// <param name="dotNetUTCDateTime"></param>
		/// <returns></returns>
		public static long TranslateDateTimeToPubnubUnixNanoSeconds(DateTime dotNetUTCDateTime)
		{
			TimeSpan timeSpan = dotNetUTCDateTime - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
			long timeStamp = Convert.ToInt64(timeSpan.TotalSeconds) * 10000000;
			return timeStamp;
		}
		
		/// <summary>
		/// Convert the Unix Nano Seconds format time to UTC/GMT DateTime
		/// </summary>
		/// <param name="unixNanoSecondTime"></param>
		/// <returns></returns>
		public static DateTime TranslatePubnubUnixNanoSecondsToDateTime(long unixNanoSecondTime)
		{
			double timeStamp = unixNanoSecondTime / 10000000;
			DateTime dateTime = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(timeStamp);
			return dateTime;
		}
		
		public void EndPendingRequests()
		{
			RemoveChannelDictionary();
			TerminatePendingWebRequest();
			TerminateHeartbeatTimer();
			TerminateReconnectTimer();
            RemoveChannelCallback();
		}
		
		public void TerminateCurrentSubscriberRequest()
		{
			string[] channels = GetCurrentSubscriberChannels();
			if (channels != null)
			{
				string multiChannel = string.Join(",", channels);
				PubnubWebRequest request = (_channelRequest.ContainsKey(multiChannel)) ? _channelRequest[multiChannel] : null;
				if (request != null)
				{
					request.Abort();
					
					//TerminateHeartbeatTimer(request.RequestUri);
					
					//TerminateReconnectTimer(multiChannel);
					
					LoggingMethod.WriteToLog(string.Format("DateTime {0} TerminateCurrentSubsciberRequest {1}", DateTime.Now.ToString(), request.RequestUri.ToString()), LoggingMethod.LevelInfo);
				}
			}
		}
		
		/// <summary>
		/// FOR TESTING ONLY - To Enable Simulation of Network Non-Availability
		/// </summary>
		public void EnableSimulateNetworkFailForTestingOnly()
		{
			ClientNetworkStatus.SimulateNetworkFailForTesting = true;
			PubnubWebRequest.SimulateNetworkFailForTesting = true;
		}
		
		/// <summary>
		/// FOR TESTING ONLY - To Disable Simulation of Network Non-Availability
		/// </summary>
		public void DisableSimulateNetworkFailForTestingOnly()
		{
			ClientNetworkStatus.SimulateNetworkFailForTesting = false;
			PubnubWebRequest.SimulateNetworkFailForTesting = false;
		}
		
		private bool IsPresenceChannel(string channel)
		{
			if (channel.LastIndexOf("-pnpres") > 0)
			{
				return true;
			}
			else
			{
				return false;
			}
		}
		
		private string[] GetCurrentSubscriberChannels()
		{
			string[] channels = null;
			if (_multiChannelSubscribe != null && _multiChannelSubscribe.Keys.Count > 0)
			{
				channels = _multiChannelSubscribe.Keys.ToArray<string>();
			}
			
			return channels;
		}

        public bool GrantAccess<T>(string channel, bool read, bool write, Action<T> userCallback, Action<PubnubClientError> errorCallback)
        {
            return GrantAccess(channel, read, write, -1, userCallback, errorCallback);
        }

        public bool GrantAccess<T>(string channel, bool read, bool write, int ttl, Action<T> userCallback, Action<PubnubClientError> errorCallback)
        {
            if (string.IsNullOrEmpty(this.secretKey) || string.IsNullOrEmpty(this.secretKey.Trim()) || this.secretKey.Length <= 0)
            {
                throw new MissingFieldException("Invalid secret key");
            }

            Uri request = BuildGrantAccessRequest(channel, read, write, ttl);

            RequestState<T> requestState = new RequestState<T>();
            requestState.Channels = new string[] { channel };
            requestState.Type = ResponseType.GrantAccess;
            requestState.UserCallback = userCallback;
            requestState.ErrorCallback = errorCallback;
            requestState.Reconnect = false;

            return UrlProcessRequest<T>(request, requestState); 

        }

        public bool GrantPresenceAccess<T>(string channel, bool read, bool write, Action<T> userCallback, Action<PubnubClientError> errorCallback)
        {
            return GrantPresenceAccess(channel, read, write, -1, userCallback, errorCallback);
        }

        public bool GrantPresenceAccess<T>(string channel, bool read, bool write, int ttl, Action<T> userCallback, Action<PubnubClientError> errorCallback)
        {
            string[] multiChannels = channel.Split(',');
            if (multiChannels.Length > 0)
            {
                for (int index = 0; index < multiChannels.Length; index++)
                {
                    multiChannels[index] = string.Format("{0}-pnpres", multiChannels[index]);
                }
            }
            string presenceChannel = string.Join(",", multiChannels);
            return GrantAccess(presenceChannel, read, write, ttl, userCallback, errorCallback);
        }

        public void AuditAccess<T>(Action<T> userCallback, Action<PubnubClientError> errorCallback)
        {
            AuditAccess("", userCallback, errorCallback);
        }

        public void AuditAccess<T>(string channel, Action<T> userCallback, Action<PubnubClientError> errorCallback)
        {
            if (string.IsNullOrEmpty(this.secretKey) || string.IsNullOrEmpty(this.secretKey.Trim()) || this.secretKey.Length <= 0)
            {
                throw new MissingFieldException("Invalid secret key");
            }

            Uri request = BuildAuditAccessRequest(channel);

            RequestState<T> requestState = new RequestState<T>();
            if (!string.IsNullOrEmpty(channel))
            {
                requestState.Channels = new string[] { channel };
            }
            requestState.Type = ResponseType.AuditAccess;
            requestState.UserCallback = userCallback;
            requestState.ErrorCallback = errorCallback;
            requestState.Reconnect = false;

            UrlProcessRequest<T>(request, requestState);
        }

        public void AuditPresenceAccess<T>(string channel, Action<T> userCallback, Action<PubnubClientError> errorCallback)
        {
            string[] multiChannels = channel.Split(',');
            if (multiChannels.Length > 0)
            {
                for (int index = 0; index < multiChannels.Length; index++)
                {
                    multiChannels[index] = string.Format("{0}-pnpres", multiChannels[index]);
                }
            }
            string presenceChannel = string.Join(",", multiChannels);
            AuditAccess(presenceChannel, userCallback, errorCallback);
        }
    }
	
	/// <summary>
	/// MD5 Service provider
	/// </summary>
	internal class MD5CryptoServiceProvider : MD5
	{
		public MD5CryptoServiceProvider()
			: base()
		{
		}
	}
	/// <summary>
	/// MD5 messaging-digest algorithm is a widely used cryptographic hash function that produces 128-bit hash value.
	/// </summary>
	internal class MD5 : IDisposable
	{
		static public MD5 Create(string hashName)
		{
			if (hashName == "MD5")
				return new MD5();
			else
				throw new NotSupportedException();
		}
		
		static public String GetMd5String(String source)
		{
			MD5 md = MD5CryptoServiceProvider.Create();
			byte[] hash;
			
			//Create a new instance of ASCIIEncoding to 
			//convert the string into an array of Unicode bytes.
			UTF8Encoding enc = new UTF8Encoding();
			
			//Convert the string into an array of bytes.
			byte[] buffer = enc.GetBytes(source);
			
			//Create the hash value from the array of bytes.
			hash = md.ComputeHash(buffer);
			
			StringBuilder sb = new StringBuilder();
			foreach (byte b in hash)
				sb.Append(b.ToString("x2"));
			return sb.ToString();
		}
		
		static public MD5 Create()
		{
			return new MD5();
		}
		
		#region base implementation of the MD5
		#region constants
		private const byte S11 = 7;
		private const byte S12 = 12;
		private const byte S13 = 17;
		private const byte S14 = 22;
		private const byte S21 = 5;
		private const byte S22 = 9;
		private const byte S23 = 14;
		private const byte S24 = 20;
		private const byte S31 = 4;
		private const byte S32 = 11;
		private const byte S33 = 16;
		private const byte S34 = 23;
		private const byte S41 = 6;
		private const byte S42 = 10;
		private const byte S43 = 15;
		private const byte S44 = 21;
		static private byte[] PADDING = new byte[] {
			0x80, 0, 0, 0, 0, 0, 
			0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
			0, 0, 0, 0, 0, 0, 0, 
			0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
			0, 0, 0, 0, 0, 0, 0, 
			0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0
		};
#endregion
		
		#region F, G, H and I are basic MD5 functions.
		static private uint F(uint x, uint y, uint z)
		{
			return (((x) & (y)) | ((~x) & (z)));
		}
		static private uint G(uint x, uint y, uint z)
		{
			return (((x) & (z)) | ((y) & (~z)));
		}
		static private uint H(uint x, uint y, uint z)
		{
			return ((x) ^ (y) ^ (z));
		}
		static private uint I(uint x, uint y, uint z)
		{
			return ((y) ^ ((x) | (~z)));
		}
#endregion
		
		#region rotates x left n bits.
		/// <summary>
		/// rotates x left n bits.
		/// </summary>
		/// <param name="x"></param>
		/// <param name="n"></param>
		/// <returns></returns>
		static private uint ROTATE_LEFT(uint x, byte n)
		{
			return (((x) << (n)) | ((x) >> (32 - (n))));
		}
#endregion
		
		#region FF, GG, HH, and II transformations
		/// FF, GG, HH, and II transformations 
		/// for rounds 1, 2, 3, and 4.
		/// Rotation is separate from addition to prevent re-computation.
		static private void FF(ref uint a, uint b, uint c, uint d, uint x, byte s, uint ac)
		{
			(a) += F((b), (c), (d)) + (x) + (uint)(ac);
			(a) = ROTATE_LEFT((a), (s));
			(a) += (b);
		}
		static private void GG(ref uint a, uint b, uint c, uint d, uint x, byte s, uint ac)
		{
			(a) += G((b), (c), (d)) + (x) + (uint)(ac);
			(a) = ROTATE_LEFT((a), (s));
			(a) += (b);
		}
		static private void HH(ref uint a, uint b, uint c, uint d, uint x, byte s, uint ac)
		{
			(a) += H((b), (c), (d)) + (x) + (uint)(ac);
			(a) = ROTATE_LEFT((a), (s));
			(a) += (b);
		}
		static private void II(ref uint a, uint b, uint c, uint d, uint x, byte s, uint ac)
		{
			(a) += I((b), (c), (d)) + (x) + (uint)(ac);
			(a) = ROTATE_LEFT((a), (s));
			(a) += (b);
		}
#endregion
		
		#region context info
		/// <summary>
		/// state (ABCD)
		/// </summary>
		uint[] state = new uint[4];
		
		/// <summary>
		/// number of bits, modulo 2^64 (LSB first)
		/// </summary>
		uint[] count = new uint[2];
		
		/// <summary>
		/// input buffer
		/// </summary>
		byte[] buffer = new byte[64];
#endregion
		
		internal MD5()
		{
			Initialize();
		}
		
		/// <summary>
		/// MD5 initialization. Begins an MD5 operation, writing a new context.
		/// </summary>
		/// <remarks>
		/// The RFC named it "MD5Init"
		/// </remarks>
		public virtual void Initialize()
		{
			count[0] = count[1] = 0;
			
			// Load magic initialization constants.
			state[0] = 0x67452301;
			state[1] = 0xefcdab89;
			state[2] = 0x98badcfe;
			state[3] = 0x10325476;
		}
		
		/// <summary>
		/// MD5 block update operation. Continues an MD5 message-digest
		/// operation, processing another message block, and updating the
		/// context.
		/// </summary>
		/// <param name="input"></param>
		/// <param name="offset"></param>
		/// <param name="count"></param>
		/// <remarks>The RFC Named it MD5Update</remarks>
		protected virtual void HashCore(byte[] input, int offset, int count)
		{
			int i;
			int index;
			int partLen;
			
			// Compute number of bytes mod 64
			index = (int)((this.count[0] >> 3) & 0x3F);
			
			// Update number of bits
			if ((this.count[0] += (uint)((uint)count << 3)) < ((uint)count << 3))
				this.count[1]++;
			this.count[1] += ((uint)count >> 29);
			
			partLen = 64 - index;
			
			// Transform as many times as possible.
			if (count >= partLen)
			{
				Buffer.BlockCopy(input, offset, this.buffer, index, partLen);
				Transform(this.buffer, 0);
				
				for (i = partLen; i + 63 < count; i += 64)
					Transform(input, offset + i);
				
				index = 0;
			}
			else
				i = 0;
			
			// Buffer remaining input 
			Buffer.BlockCopy(input, offset + i, this.buffer, index, count - i);
		}
		
		/// <summary>
		/// MD5 finalization. Ends an MD5 message-digest operation, writing the
		/// the message digest and zeroizing the context.
		/// </summary>
		/// <returns>message digest</returns>
		/// <remarks>The RFC named it MD5Final</remarks>
		protected virtual byte[] HashFinal()
		{
			byte[] digest = new byte[16];
			byte[] bits = new byte[8];
			int index, padLen;
			
			// Save number of bits
			Encode(bits, 0, this.count, 0, 8);
			
			// Pad out to 56 mod 64.
			index = (int)((uint)(this.count[0] >> 3) & 0x3f);
			padLen = (index < 56) ? (56 - index) : (120 - index);
			HashCore(PADDING, 0, padLen);
			
			// Append length (before padding)
			HashCore(bits, 0, 8);
			
			// Store state in digest 
			Encode(digest, 0, state, 0, 16);
			
			// Zeroize sensitive information.
			count[0] = count[1] = 0;
			state[0] = 0;
			state[1] = 0;
			state[2] = 0;
			state[3] = 0;
			
			// initialize again, to be ready to use
			Initialize();
			
			return digest;
		}
		
		/// <summary>
		/// MD5 basic transformation. Transforms state based on 64 bytes block.
		/// </summary>
		/// <param name="block"></param>
		/// <param name="offset"></param>
		private void Transform(byte[] block, int offset)
		{
			uint a = state[0], b = state[1], c = state[2], d = state[3];
			uint[] x = new uint[16];
			Decode(x, 0, block, offset, 64);
			
			// Round 1
			FF(ref a, b, c, d, x[0], S11, 0xd76aa478); /* 1 */
			FF(ref d, a, b, c, x[1], S12, 0xe8c7b756); /* 2 */
			FF(ref c, d, a, b, x[2], S13, 0x242070db); /* 3 */
			FF(ref b, c, d, a, x[3], S14, 0xc1bdceee); /* 4 */
			FF(ref a, b, c, d, x[4], S11, 0xf57c0faf); /* 5 */
			FF(ref d, a, b, c, x[5], S12, 0x4787c62a); /* 6 */
			FF(ref c, d, a, b, x[6], S13, 0xa8304613); /* 7 */
			FF(ref b, c, d, a, x[7], S14, 0xfd469501); /* 8 */
			FF(ref a, b, c, d, x[8], S11, 0x698098d8); /* 9 */
			FF(ref d, a, b, c, x[9], S12, 0x8b44f7af); /* 10 */
			FF(ref c, d, a, b, x[10], S13, 0xffff5bb1); /* 11 */
			FF(ref b, c, d, a, x[11], S14, 0x895cd7be); /* 12 */
			FF(ref a, b, c, d, x[12], S11, 0x6b901122); /* 13 */
			FF(ref d, a, b, c, x[13], S12, 0xfd987193); /* 14 */
			FF(ref c, d, a, b, x[14], S13, 0xa679438e); /* 15 */
			FF(ref b, c, d, a, x[15], S14, 0x49b40821); /* 16 */
			
			// Round 2
			GG(ref a, b, c, d, x[1], S21, 0xf61e2562); /* 17 */
			GG(ref d, a, b, c, x[6], S22, 0xc040b340); /* 18 */
			GG(ref c, d, a, b, x[11], S23, 0x265e5a51); /* 19 */
			GG(ref b, c, d, a, x[0], S24, 0xe9b6c7aa); /* 20 */
			GG(ref a, b, c, d, x[5], S21, 0xd62f105d); /* 21 */
			GG(ref d, a, b, c, x[10], S22, 0x2441453); /* 22 */
			GG(ref c, d, a, b, x[15], S23, 0xd8a1e681); /* 23 */
			GG(ref b, c, d, a, x[4], S24, 0xe7d3fbc8); /* 24 */
			GG(ref a, b, c, d, x[9], S21, 0x21e1cde6); /* 25 */
			GG(ref d, a, b, c, x[14], S22, 0xc33707d6); /* 26 */
			GG(ref c, d, a, b, x[3], S23, 0xf4d50d87); /* 27 */
			GG(ref b, c, d, a, x[8], S24, 0x455a14ed); /* 28 */
			GG(ref a, b, c, d, x[13], S21, 0xa9e3e905); /* 29 */
			GG(ref d, a, b, c, x[2], S22, 0xfcefa3f8); /* 30 */
			GG(ref c, d, a, b, x[7], S23, 0x676f02d9); /* 31 */
			GG(ref b, c, d, a, x[12], S24, 0x8d2a4c8a); /* 32 */
			
			// Round 3
			HH(ref a, b, c, d, x[5], S31, 0xfffa3942); /* 33 */
			HH(ref d, a, b, c, x[8], S32, 0x8771f681); /* 34 */
			HH(ref c, d, a, b, x[11], S33, 0x6d9d6122); /* 35 */
			HH(ref b, c, d, a, x[14], S34, 0xfde5380c); /* 36 */
			HH(ref a, b, c, d, x[1], S31, 0xa4beea44); /* 37 */
			HH(ref d, a, b, c, x[4], S32, 0x4bdecfa9); /* 38 */
			HH(ref c, d, a, b, x[7], S33, 0xf6bb4b60); /* 39 */
			HH(ref b, c, d, a, x[10], S34, 0xbebfbc70); /* 40 */
			HH(ref a, b, c, d, x[13], S31, 0x289b7ec6); /* 41 */
			HH(ref d, a, b, c, x[0], S32, 0xeaa127fa); /* 42 */
			HH(ref c, d, a, b, x[3], S33, 0xd4ef3085); /* 43 */
			HH(ref b, c, d, a, x[6], S34, 0x4881d05); /* 44 */
			HH(ref a, b, c, d, x[9], S31, 0xd9d4d039); /* 45 */
			HH(ref d, a, b, c, x[12], S32, 0xe6db99e5); /* 46 */
			HH(ref c, d, a, b, x[15], S33, 0x1fa27cf8); /* 47 */
			HH(ref b, c, d, a, x[2], S34, 0xc4ac5665); /* 48 */
			
			// Round 4
			II(ref a, b, c, d, x[0], S41, 0xf4292244); /* 49 */
			II(ref d, a, b, c, x[7], S42, 0x432aff97); /* 50 */
			II(ref c, d, a, b, x[14], S43, 0xab9423a7); /* 51 */
			II(ref b, c, d, a, x[5], S44, 0xfc93a039); /* 52 */
			II(ref a, b, c, d, x[12], S41, 0x655b59c3); /* 53 */
			II(ref d, a, b, c, x[3], S42, 0x8f0ccc92); /* 54 */
			II(ref c, d, a, b, x[10], S43, 0xffeff47d); /* 55 */
			II(ref b, c, d, a, x[1], S44, 0x85845dd1); /* 56 */
			II(ref a, b, c, d, x[8], S41, 0x6fa87e4f); /* 57 */
			II(ref d, a, b, c, x[15], S42, 0xfe2ce6e0); /* 58 */
			II(ref c, d, a, b, x[6], S43, 0xa3014314); /* 59 */
			II(ref b, c, d, a, x[13], S44, 0x4e0811a1); /* 60 */
			II(ref a, b, c, d, x[4], S41, 0xf7537e82); /* 61 */
			II(ref d, a, b, c, x[11], S42, 0xbd3af235); /* 62 */
			II(ref c, d, a, b, x[2], S43, 0x2ad7d2bb); /* 63 */
			II(ref b, c, d, a, x[9], S44, 0xeb86d391); /* 64 */
			
			state[0] += a;
			state[1] += b;
			state[2] += c;
			state[3] += d;
			
			// Zeroize sensitive information.
			for (int i = 0; i < x.Length; i++)
				x[i] = 0;
		}
		
		/// <summary>
		/// Encodes input (uint) into output (byte). Assumes len is
		///  multiple of 4.
		/// </summary>
		/// <param name="output"></param>
		/// <param name="outputOffset"></param>
		/// <param name="input"></param>
		/// <param name="inputOffset"></param>
		/// <param name="count"></param>
		private static void Encode(byte[] output, int outputOffset, uint[] input, int inputOffset, int count)
		{
			int i, j;
			int end = outputOffset + count;
			for (i = inputOffset, j = outputOffset; j < end; i++, j += 4)
			{
				output[j] = (byte)(input[i] & 0xff);
				output[j + 1] = (byte)((input[i] >> 8) & 0xff);
				output[j + 2] = (byte)((input[i] >> 16) & 0xff);
				output[j + 3] = (byte)((input[i] >> 24) & 0xff);
			}
		}
		
		/// <summary>
		/// Decodes input (byte) into output (uint). Assumes len is
		/// a multiple of 4.
		/// </summary>
		/// <param name="output"></param>
		/// <param name="outputOffset"></param>
		/// <param name="input"></param>
		/// <param name="inputOffset"></param>
		/// <param name="count"></param>
		static private void Decode(uint[] output, int outputOffset, byte[] input, int inputOffset, int count)
		{
			int i, j;
			int end = inputOffset + count;
			for (i = outputOffset, j = inputOffset; j < end; i++, j += 4)
				output[i] = ((uint)input[j]) | (((uint)input[j + 1]) << 8) | (((uint)input[j + 2]) << 16) | (((uint)input[j + 3]) <<
				                                                                                             24);
		}
#endregion
		
		#region expose the same interface as the regular MD5 object
		
		protected byte[] HashValue;
		protected int State;
		public virtual bool CanReuseTransform
		{
			get
			{
				return true;
			}
		}
		
		public virtual bool CanTransformMultipleBlocks
		{
			get
			{
				return true;
			}
		}
		public virtual byte[] Hash
		{
			get
			{
				if (this.State != 0)
					throw new InvalidOperationException();
				return (byte[])HashValue.Clone();
			}
		}
		public virtual int HashSize
		{
			get
			{
				return HashSizeValue;
			}
		}
		protected int HashSizeValue = 128;
		
		public virtual int InputBlockSize
		{
			get
			{
				return 1;
			}
		}
		public virtual int OutputBlockSize
		{
			get
			{
				return 1;
			}
		}
		
		public void Clear()
		{
			Dispose(true);
		}
		
		public byte[] ComputeHash(byte[] buffer)
		{
			return ComputeHash(buffer, 0, buffer.Length);
		}
		public byte[] ComputeHash(byte[] buffer, int offset, int count)
		{
			Initialize();
			HashCore(buffer, offset, count);
			HashValue = HashFinal();
			return (byte[])HashValue.Clone();
		}
		
		public byte[] ComputeHash(Stream inputStream)
		{
			Initialize();
			int count;
			byte[] buffer = new byte[4096];
			while (0 < (count = inputStream.Read(buffer, 0, 4096)))
			{
				HashCore(buffer, 0, count);
			}
			HashValue = HashFinal();
			return (byte[])HashValue.Clone();
		}
		
		public int TransformBlock(
			byte[] inputBuffer,
			int inputOffset,
			int inputCount,
			byte[] outputBuffer,
			int outputOffset
			)
		{
			if (inputBuffer == null)
			{
				throw new ArgumentNullException("inputBuffer");
			}
			if (inputOffset < 0)
			{
				throw new ArgumentOutOfRangeException("inputOffset");
			}
			if ((inputCount < 0) || (inputCount > inputBuffer.Length))
			{
				throw new ArgumentException("inputCount");
			}
			if ((inputBuffer.Length - inputCount) < inputOffset)
			{
				throw new ArgumentOutOfRangeException("inputOffset");
			}
			if (this.State == 0)
			{
				Initialize();
				this.State = 1;
			}
			
			HashCore(inputBuffer, inputOffset, inputCount);
			if ((inputBuffer != outputBuffer) || (inputOffset != outputOffset))
			{
				Buffer.BlockCopy(inputBuffer, inputOffset, outputBuffer, outputOffset, inputCount);
			}
			return inputCount;
		}
		public byte[] TransformFinalBlock(
			byte[] inputBuffer,
			int inputOffset,
			int inputCount
			)
		{
			if (inputBuffer == null)
			{
				throw new ArgumentNullException("inputBuffer");
			}
			if (inputOffset < 0)
			{
				throw new ArgumentOutOfRangeException("inputOffset");
			}
			if ((inputCount < 0) || (inputCount > inputBuffer.Length))
			{
				throw new ArgumentException("inputCount");
			}
			if ((inputBuffer.Length - inputCount) < inputOffset)
			{
				throw new ArgumentOutOfRangeException("inputOffset");
			}
			if (this.State == 0)
			{
				Initialize();
			}
			HashCore(inputBuffer, inputOffset, inputCount);
			HashValue = HashFinal();
			byte[] buffer = new byte[inputCount];
			Buffer.BlockCopy(inputBuffer, inputOffset, buffer, 0, inputCount);
			this.State = 0;
			return buffer;
		}
#endregion
		
		protected virtual void Dispose(bool disposing)
		{
			if (!disposing)
				Initialize();
		}
		public void Dispose()
		{
			Dispose(true);
		}
	}
	
	public class PubnubCrypto
	{
		private string cipherKey = "";
		public PubnubCrypto(string cipher_key)
		{
			this.cipherKey = cipher_key;
		}
		
		/// <summary>
		/// Computes the hash using the specified algo
		/// </summary>
		/// <returns>
		/// The hash.
		/// </returns>
		/// <param name='input'>
		/// Input string
		/// </param>
		/// <param name='algorithm'>
		/// Algorithm to use for Hashing
		/// </param>
		private static string ComputeHash(string input, HashAlgorithm algorithm)
		{
#if (SILVERLIGHT || WINDOWS_PHONE)
			Byte[] inputBytes = System.Text.Encoding.UTF8.GetBytes(input);
#else
			Byte[] inputBytes = System.Text.Encoding.ASCII.GetBytes(input);
#endif
			Byte[] hashedBytes = algorithm.ComputeHash(inputBytes);
			return BitConverter.ToString(hashedBytes);
		}
		
		private string GetEncryptionKey()
		{
            //Compute Hash using the SHA256 
#if (SILVERLIGHT || WINDOWS_PHONE || MONOTOUCH || __IOS__ || MONODROID || __ANDROID__ || UNITY_STANDALONE || UNITY_WEBPLAYER || UNITY_IOS || UNITY_ANDROID)
			string strKeySHA256HashRaw = ComputeHash(this.cipherKey, new System.Security.Cryptography.SHA256Managed());
#else
            string strKeySHA256HashRaw = ComputeHash(this.cipherKey, new SHA256CryptoServiceProvider());
#endif
			//delete the "-" that appear after every 2 chars
			string strKeySHA256Hash = (strKeySHA256HashRaw.Replace("-", "")).Substring(0, 32);
			//convert to lower case
			return strKeySHA256Hash.ToLower();
		}
		
		/**
         * EncryptOrDecrypt
         * 
         * Basic function for encrypt or decrypt a string
         * for encrypt type = true
         * for decrypt type = false
         */
		private string EncryptOrDecrypt(bool type, string plainStr)
		{
#if (SILVERLIGHT || WINDOWS_PHONE)
			AesManaged aesEncryption = new AesManaged();
			aesEncryption.KeySize = 256;
			aesEncryption.BlockSize = 128;
			//get ASCII bytes of the string
			aesEncryption.IV = System.Text.Encoding.UTF8.GetBytes("0123456789012345");
			aesEncryption.Key = System.Text.Encoding.UTF8.GetBytes(GetEncryptionKey());
#else
			RijndaelManaged aesEncryption = new RijndaelManaged();
			aesEncryption.KeySize = 256;
			aesEncryption.BlockSize = 128;
			//Mode CBC
			aesEncryption.Mode = CipherMode.CBC;
			//padding
			aesEncryption.Padding = PaddingMode.PKCS7;
			//get ASCII bytes of the string
			aesEncryption.IV = System.Text.Encoding.ASCII.GetBytes("0123456789012345");
			aesEncryption.Key = System.Text.Encoding.ASCII.GetBytes(GetEncryptionKey());
#endif
			
			if (type)
			{
				ICryptoTransform crypto = aesEncryption.CreateEncryptor();
				plainStr = EncodeNonAsciiCharacters(plainStr);
#if (SILVERLIGHT || WINDOWS_PHONE)
				byte[] plainText = Encoding.UTF8.GetBytes(plainStr);
#else
				byte[] plainText = Encoding.ASCII.GetBytes(plainStr);
#endif
				
				//encrypt
				byte[] cipherText = crypto.TransformFinalBlock(plainText, 0, plainText.Length);
				return Convert.ToBase64String(cipherText);
			}
			else
			{
				try
				{
					ICryptoTransform decrypto = aesEncryption.CreateDecryptor();
					//decode
					byte[] decryptedBytes = Convert.FromBase64CharArray(plainStr.ToCharArray(), 0, plainStr.Length);
					
					//decrypt
#if (SILVERLIGHT || WINDOWS_PHONE)
					var data = decrypto.TransformFinalBlock(decryptedBytes, 0, decryptedBytes.Length);
					string decrypted = Encoding.UTF8.GetString(data, 0, data.Length);
#else
					string decrypted = System.Text.Encoding.ASCII.GetString(decrypto.TransformFinalBlock(decryptedBytes, 0, decryptedBytes.Length));
#endif
					
					return decrypted;
				}
				catch (Exception ex)
				{
					throw ex;
					//LoggingMethod.WriteToLog(string.Format("DateTime {0} Decrypt Error. {1}", DateTime.Now.ToString(), ex.ToString()), LoggingMethod.LevelVerbose);
					//return "**DECRYPT ERROR**";
				}
			}
		}
		
		// encrypt string
		public string Encrypt(string plainText)
		{
			if (plainText == null || plainText.Length <= 0) throw new ArgumentNullException("plainText");
			
			return EncryptOrDecrypt(true, plainText);
		}
		
		// decrypt string
		public string Decrypt(string cipherText)
		{
			if (cipherText == null) throw new ArgumentNullException("cipherText");
			
			return EncryptOrDecrypt(false, cipherText);
		}
		
		//md5 used for AES encryption key
		private static byte[] Md5(string cipherKey)
		{
			MD5 obj = new MD5CryptoServiceProvider();
#if (SILVERLIGHT || WINDOWS_PHONE)
			byte[] data = Encoding.UTF8.GetBytes(cipherKey);
#else
			byte[] data = Encoding.Default.GetBytes(cipherKey);
#endif
			return obj.ComputeHash(data);
		}
		
		/// <summary>
		/// Converts the upper case hex to lower case hex.
		/// </summary>
		/// <returns>The lower case hex.</returns>
		/// <param name="value">Hex Value.</param>
		private string ConvertHexToUnicodeChars( string value ) {
			//if(;
			return Regex.Replace(
				value,
				@"\\u(?<Value>[a-zA-Z0-9]{4})",
				m => {
				return ((char) int.Parse( m.Groups["Value"].Value, NumberStyles.HexNumber )).ToString();
			}     
			);
		}
		
		/// <summary>
		/// Encodes the non ASCII characters.
		/// </summary>
		/// <returns>
		/// The non ASCII characters.
		/// </returns>
		/// <param name='value'>
		/// Value.
		/// </param>
		private string EncodeNonAsciiCharacters(string value)
		{
#if (USE_JSONFX)
			value = ConvertHexToUnicodeChars(value);
#endif
			
			StringBuilder sb = new StringBuilder();
			foreach (char c in value)
			{
				if (c > 127)
				{
					// This character is too big for ASCII
					string encodedValue = "\\u" + ((int)c).ToString("x4");
					sb.Append(encodedValue);
				}
				else
				{
					sb.Append(c);
				}
			}
			return sb.ToString();
		}

        public string PubnubAccessManagerSign(string key, string data)
        {
            string secret = key;
            string message = data;

            var encoding = new System.Text.UTF8Encoding();
            byte[] keyByte = encoding.GetBytes(secret);
            byte[] messageBytes = encoding.GetBytes(message);

            using (var hmacsha256 = new HMACSHA256(keyByte))
            {
                byte[] hashmessage = hmacsha256.ComputeHash(messageBytes);
                return Convert.ToBase64String(hashmessage).Replace('+', '-').Replace('/', '_'); ;
            }
        }

		
	}
	
	internal enum ResponseType
	{
		Publish,
		History,
		Time,
		Subscribe,
		Presence,
		Here_Now,
		DetailedHistory,
		Leave,
		Unsubscribe,
		PresenceUnsubscribe,
        GrantAccess,
        AuditAccess,
        RevokeAccess
	}
	
	internal class ReconnectState<T>
	{
		public string[] Channels;
		public ResponseType Type;
		public Action<T> Callback;
		public Action<PubnubClientError> ErrorCallback;
		public Action<T> ConnectCallback;
		public object Timetoken;
		
		public ReconnectState()
		{
			Channels = null;
			Callback = null;
			ConnectCallback = null;
			Timetoken = null;
		}
	}
	
	internal class RequestState<T>
	{
		public Action<T> UserCallback;
		public Action<PubnubClientError> ErrorCallback;
		public Action<T> ConnectCallback;
		public PubnubWebRequest Request;
		public PubnubWebResponse Response;
		public ResponseType Type;
		public string[] Channels;
		public bool Timeout;
		public bool Reconnect;
		public long Timetoken;
		
		public RequestState()
		{
			UserCallback = null;
			ConnectCallback = null;
			Request = null;
			Response = null;
			Channels = null;
		}
	}

    internal class PubnubChannelCallback<T>
    {
        public Action<T> Callback;
        public Action<PubnubClientError> ErrorCallback;
        public Action<T> ConnectCallback;
        public Action<T> DisconnectCallback;
        //public ResponseType Type;

        public PubnubChannelCallback()
        {
            Callback = null;
            ConnectCallback = null;
            DisconnectCallback = null;
            ErrorCallback = null;
        }
    }
	
	internal class InternetState<T>
	{
		public Action<bool> Callback;
		public Action<PubnubClientError> ErrorCallback;
		public string[] Channels;
		
		public InternetState()
		{
			Callback = null;
			ErrorCallback = null;
			Channels = null;
		}
	}
	
	internal class ClientNetworkStatus
	{
		private static bool _status = true;
		private static bool _failClientNetworkForTesting = false;
		
		private static IJsonPluggableLibrary _jsonPluggableLibrary;
		internal static IJsonPluggableLibrary JsonPluggableLibrary
		{
			get
			{
				return _jsonPluggableLibrary;
			}
			set
			{
				_jsonPluggableLibrary = value;
			}
		}
		
#if (SILVERLIGHT  || WINDOWS_PHONE)
		private static ManualResetEvent mres = new ManualResetEvent(false);
		private static ManualResetEvent mreSocketAsync = new ManualResetEvent(false);
#else
		private static ManualResetEventSlim mres = new ManualResetEventSlim(false);
#endif
		internal static bool SimulateNetworkFailForTesting
		{
			get
			{
				return _failClientNetworkForTesting;
			}
			
			set
			{
				_failClientNetworkForTesting = value;
			}
			
		}
		
		internal static bool CheckInternetStatus<T>(bool systemActive, Action<PubnubClientError> errorCallback, string[] channels)
		{
			if (_failClientNetworkForTesting)
			{
				//Only to simulate network fail
				return false;
			}
			else
			{
				CheckClientNetworkAvailability<T>(CallbackClientNetworkStatus, errorCallback, channels);
				return _status;
			}
		}
		
		
		private static void CallbackClientNetworkStatus(bool status)
		{
			_status = status;
		}
		
		private static void CheckClientNetworkAvailability<T>(Action<bool> callback, Action<PubnubClientError> errorCallback, string[] channels)
		{
			InternetState<T> state = new InternetState<T>();
			state.Callback = callback;
			state.ErrorCallback = errorCallback;
			state.Channels = channels;
			ThreadPool.QueueUserWorkItem(CheckSocketConnect<T>, state);
#if (SILVERLIGHT || WINDOWS_PHONE)
			mres.WaitOne();
#else
			mres.Wait();
#endif
		}
		
		private static void CheckSocketConnect<T>(object internetState)
		{
			InternetState<T> state = internetState as InternetState<T>;
			Action<bool> callback = state.Callback;
			Action<PubnubClientError> errorCallback = state.ErrorCallback;
			string[] channels = state.Channels;
			try
			{
#if (SILVERLIGHT || WINDOWS_PHONE)
				using (Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp))
				{
					SocketAsyncEventArgs sae = new SocketAsyncEventArgs();
					sae.UserToken = state;
					sae.RemoteEndPoint = new DnsEndPoint("pubsub.pubnub.com", 80);
					sae.Completed += new EventHandler<SocketAsyncEventArgs>(socketAsync_Completed<T>);
					bool test = socket.ConnectAsync(sae);
					
					mreSocketAsync.WaitOne(1000);
					sae.Completed -= new EventHandler<SocketAsyncEventArgs>(socketAsync_Completed<T>);
					socket.Close();
				}
#else
				using (UdpClient udp = new UdpClient("pubsub.pubnub.com", 80))
				{
					IPAddress localAddress = ((IPEndPoint)udp.Client.LocalEndPoint).Address;
					EndPoint remotepoint = udp.Client.RemoteEndPoint;
					string remoteAddress = (remotepoint != null) ? remotepoint.ToString() : "";
					udp.Close();
					
					LoggingMethod.WriteToLog(string.Format("DateTime {0} checkInternetStatus LocalIP: {1}, RemoteEndPoint:{2}", DateTime.Now.ToString(), localAddress.ToString(), remoteAddress), LoggingMethod.LevelVerbose);
					callback(true);
				}
#endif
			}
			catch (Exception ex)
			{
                PubnubErrorCode errorType = PubnubErrorCodeHelper.GetErrorType(ex);
                int statusCode = (int)errorType;
                string errorDescription = PubnubErrorCodeDescription.GetStatusCodeDescription(errorType);
                PubnubClientError error = new PubnubClientError(statusCode, PubnubErrorSeverity.Warn, true, ex.Message, ex, PubnubMessageSource.Client, null, null, errorDescription, string.Join(",", channels));
                GoToCallback(error, errorCallback);
				
				LoggingMethod.WriteToLog(string.Format("DateTime {0} checkInternetStatus Error. {1}", DateTime.Now.ToString(), ex.ToString()), LoggingMethod.LevelError);
				callback(false);
			}
			mres.Set();
		}

        private static void GoToCallback<T>(object result, Action<T> Callback)
        {
            if (Callback != null)
            {
                if (typeof(T) == typeof(string))
                {
                    JsonResponseToCallback(result, Callback);
                }
                else
                {
                    Callback((T)(object)result);
                }
            }
        }

        private static void GoToCallback<T>(PubnubClientError result, Action<PubnubClientError> Callback)
        {
            if (Callback != null)
            {
                //TODO:
                //Include custom message related to error/status code for developer
                //error.AdditionalMessage = MyCustomErrorMessageRetrieverBasedOnStatusCode(error.StatusCode);

                Callback(result);
            }
        }


        private static void JsonResponseToCallback<T>(object result, Action<T> callback)
        {
            string callbackJson = "";

            if (typeof(T) == typeof(string))
            {
                callbackJson = _jsonPluggableLibrary.SerializeToJsonString(result);

                Action<string> castCallback = callback as Action<string>;
                castCallback(callbackJson);
            }
        }
		
#if (SILVERLIGHT || WINDOWS_PHONE)
		static void socketAsync_Completed<T>(object sender, SocketAsyncEventArgs e)
		{
			if (e.LastOperation == SocketAsyncOperation.Connect)
			{
				Socket skt = sender as Socket;
				InternetState<T> state = e.UserToken as InternetState<T>;
				if (state != null)
				{
					LoggingMethod.WriteToLog(string.Format("DateTime {0} socketAsync_Completed.", DateTime.Now.ToString()), LoggingMethod.LevelInfo);
					state.Callback(true);
				}
				mreSocketAsync.Set();
			}
		}
#endif
		
	}
	
#if (UNITY_STANDALONE || UNITY_WEBPLAYER || UNITY_IOS || UNITY_ANDROID)
	internal class LoggingMethod:MonoBehaviour
#else
		internal class LoggingMethod
#endif
	{
		private static int logLevel = 0;
		public static Level LogLevel
		{
			get
			{
				return (Level)logLevel;
			}
			set
			{
				logLevel = (int)value;
			}
		}
		public enum Level
		{
			Off,
			Error,
			Info,
			Verbose,
			Warning
		}
		
		public static bool LevelError
		{
			get
			{
				return (int)LogLevel >= 1;
			}
		}
		
		public static bool LevelInfo
		{
			get
			{
				return (int)LogLevel >= 2;
			}
		}
		
		public static bool LevelVerbose
		{
			get
			{
				return (int)LogLevel >= 3;
			}
		}
		
		public static bool LevelWarning
		{
			get
			{
				return (int)LogLevel >= 4;
			}
		}
		
		public static void WriteToLog(string logText, bool writeToLog)
		{
			if (writeToLog)
            {
#if (SILVERLIGHT || WINDOWS_PHONE || MONOTOUCH || __IOS__ || MONODROID || __ANDROID__ || UNITY_IOS || UNITY_ANDROID)
				System.Diagnostics.Debug.WriteLine(logText);
#elif (UNITY_STANDALONE || UNITY_WEBPLAYER)
				print(logText);
#else
                Trace.WriteLine(logText);
                Trace.Flush();
#endif
			}
		}
	}
	
	public interface IPubnubUnitTest
	{
		bool EnableStubTest
		{
			get;
			set;
		}
		
		string TestClassName
		{
			get;
			set;
		}
		
		string TestCaseName
		{
			get;
			set;
		}
		
		string GetStubResponse(HttpWebRequest request);
	}
	
	internal class PubnubWebRequestCreator : IWebRequestCreate
	{
		private IPubnubUnitTest pubnubUnitTest = null;
		public PubnubWebRequestCreator()
		{
		}
		
		public PubnubWebRequestCreator(IPubnubUnitTest pubnubUnitTest)
		{
			this.pubnubUnitTest = pubnubUnitTest;
		}
		
		public WebRequest Create(Uri uri)
		{
			HttpWebRequest req = (HttpWebRequest)WebRequest.Create(uri);
            OperatingSystem userOS = System.Environment.OSVersion;
            req.UserAgent = string.Format("ua_string=({0}) PubNub-csharp/3.5", userOS.VersionString);
            if (this.pubnubUnitTest is IPubnubUnitTest)
			{
				return new PubnubWebRequest(req, pubnubUnitTest);
			}
			else
			{
				return new PubnubWebRequest(req);
			}
		}
	}

    public class PubnubWebRequest : WebRequest
	{
		private IPubnubUnitTest pubnubUnitTest = null;
		private static bool simulateNetworkFailForTesting = false;
        private bool terminated = false;
		
		HttpWebRequest request;

        internal static bool SimulateNetworkFailForTesting
		{
			get
			{
				return simulateNetworkFailForTesting;
			}
			set
			{
				simulateNetworkFailForTesting = value;
			}
		}
		
#if (!SILVERLIGHT && !WINDOWS_PHONE)
		private int _timeout;
        public override int Timeout
		{
			get
			{
				return _timeout;
			}
			set
			{
				_timeout = value;
				if (request != null)
				{
					request.Timeout = _timeout;
				}
			}
		}

        public override IWebProxy Proxy
		{
			get
			{
				return request.Proxy;
			}
			set
			{
				request.Proxy = value;
			}
		}

        public override bool PreAuthenticate
		{
			get
			{
				return request.PreAuthenticate;
			}
			set
			{
				request.PreAuthenticate = value;
			}
		}
#endif
		
#if ((!__MonoCS__) && (!SILVERLIGHT) && !WINDOWS_PHONE)
        public ServicePoint ServicePoint;
#endif
		
		public PubnubWebRequest(HttpWebRequest request)
		{
			this.request = request;
#if ((!__MonoCS__) && (!SILVERLIGHT) && !WINDOWS_PHONE)
			this.ServicePoint = this.request.ServicePoint;
#endif
		}
		public PubnubWebRequest(HttpWebRequest request, IPubnubUnitTest pubnubUnitTest)
		{
			this.request = request;
			this.pubnubUnitTest = pubnubUnitTest;
#if ((!__MonoCS__) && (!SILVERLIGHT) && !WINDOWS_PHONE)
			this.ServicePoint = this.request.ServicePoint;
#endif
		}
		
		public override void Abort()
		{
			if (request != null)
			{
                terminated = true;
                request.Abort();
			}
		}

        public override WebHeaderCollection Headers
		{
			get
			{
				return request.Headers;
			}
			set
			{
				request.Headers = value;
			}
		}

        public override string Method
		{
			get
			{
				return request.Method;
			}
			set
			{
				request.Method = value;
			}
		}

        public override string ContentType
		{
			get
			{
				return request.ContentType;
			}
			set
			{
				request.ContentType = value;
			}
		}

        public override ICredentials Credentials
		{
			get
			{
				return request.Credentials;
			}
			set
			{
				request.Credentials = value;
			}
		}
		
		public override IAsyncResult BeginGetRequestStream(AsyncCallback callback, object state)
		{
			return request.BeginGetRequestStream(callback, state);
		}
		
		public override Stream EndGetRequestStream(IAsyncResult asyncResult)
		{
			return request.EndGetRequestStream(asyncResult);
		}
		
		public override IAsyncResult BeginGetResponse(AsyncCallback callback, object state)
		{
			if (pubnubUnitTest is IPubnubUnitTest && pubnubUnitTest.EnableStubTest)
			{
				return new PubnubWebAsyncResult(callback, state);
			}
			else
			{
				return request.BeginGetResponse(callback, state);
			}
		}
		
		public override WebResponse EndGetResponse(IAsyncResult asyncResult)
		{
			if (pubnubUnitTest is IPubnubUnitTest && pubnubUnitTest.EnableStubTest)
			{
				string stubResponse = pubnubUnitTest.GetStubResponse(request);
				return new PubnubWebResponse(new MemoryStream(Encoding.UTF8.GetBytes(stubResponse)));
			}
			else if (simulateNetworkFailForTesting)
			{
				WebException simulateException = new WebException("For simulating network fail, the remote name could not be resolved", WebExceptionStatus.ConnectFailure);
				throw simulateException;
			}
			else
			{
				return new PubnubWebResponse(request.EndGetResponse(asyncResult));
			}
		}

        public override Uri RequestUri
		{
			get
			{
				return request.RequestUri;
			}
		}

        public override string ConnectionGroupName
        {
            get
            {
                return request.ConnectionGroupName;
            }
        }

        public override long ContentLength
        {
            get
            {
                return request.ContentLength;
            }
        }

        public override bool UseDefaultCredentials
        {
            get
            {
                return request.UseDefaultCredentials;
            }
        }

        public override System.Net.Cache.RequestCachePolicy CachePolicy
        {
            get
            {
                return request.CachePolicy;
            }
        }

        public bool Terminated
        {
            get
            {
                return terminated;
            }
        }
    }

    public class PubnubWebResponse : WebResponse
	{
		WebResponse response;
		readonly Stream _responseStream;
		HttpStatusCode httpStatusCode;
		
		public PubnubWebResponse(WebResponse response)
		{
			this.response = response;
		}
		
		public PubnubWebResponse(WebResponse response, HttpStatusCode statusCode)
		{
			this.response = response;
			this.httpStatusCode = statusCode;
		}
		
		public PubnubWebResponse(Stream responseStream)
		{
			_responseStream = responseStream;
		}
		
		public PubnubWebResponse(Stream responseStream, HttpStatusCode statusCode)
		{
			_responseStream = responseStream;
			this.httpStatusCode = statusCode;
		}
		
		public override Stream GetResponseStream()
		{
			if (response != null)
				return response.GetResponseStream();
			else
				return _responseStream;
		}
		
		public override void Close()
		{
			if (response != null)
			{
				response.Close();
			}
		}

        public override WebHeaderCollection Headers
        {
            get
            {
                return response.Headers;
            }
        }

        public override long ContentLength
		{
			get
			{
				return response.ContentLength;
			}
		}

        public override string ContentType
		{
			get
			{
				return response.ContentType;
			}
		}

        public override Uri ResponseUri
		{
			get
			{
				return response.ResponseUri;
			}
		}

        public HttpStatusCode HttpStatusCode
		{
			get
			{
				return httpStatusCode;
			}
		}

        public override bool IsFromCache
        {
            get
            {
                return response.IsFromCache;
            }
        }

        public override bool IsMutuallyAuthenticated
        {
            get
            {
                return response.IsMutuallyAuthenticated;
            }
        }

	}
	
	internal class PubnubWebAsyncResult : IAsyncResult
	{
		private const int pubnubDefaultLatencyInMilliSeconds = 1; //PubnubDefaultLatencyInMilliSeconds
		private readonly AsyncCallback _callback;
		private readonly object _state;
		private readonly ManualResetEvent _waitHandle;
		private readonly Timer _timer;
		
		public bool IsCompleted 
		{ 
			get; private set; 
		}
		
		public WaitHandle AsyncWaitHandle
		{
			get { return _waitHandle; }
		}
		
		public object AsyncState
		{
			get { return _state; }
		}
		
		public bool CompletedSynchronously
		{
			get { return IsCompleted; }
		}
		
		public PubnubWebAsyncResult(AsyncCallback callback, object state)
			: this(callback, state, TimeSpan.FromMilliseconds(pubnubDefaultLatencyInMilliSeconds))
		{
		}
		
		public PubnubWebAsyncResult(AsyncCallback callback, object state, TimeSpan latency)
		{
			IsCompleted = false;
			_callback = callback;
			_state = state;
			_waitHandle = new ManualResetEvent(false);
			_timer = new Timer(onTimer => NotifyComplete(), null, latency, TimeSpan.FromMilliseconds(-1));
		}
		
		public void Abort()
		{
			_timer.Dispose();
			NotifyComplete();
		}
		
		private void NotifyComplete()
		{
			IsCompleted = true;
			_waitHandle.Set();
			if (_callback != null)
				_callback(this);
		}
	}
	
	public class PubnubProxy
	{
		string proxyServer;
		int proxyPort;
		string proxyUserName;
		string proxyPassword;
		
		public string ProxyServer
		{
			get
			{
				return proxyServer;
			}
			set
			{
				proxyServer = value;
			}
		}
		
		public int ProxyPort
		{
			get
			{
				return proxyPort;
			}
			set
			{
				proxyPort = value;
			}
		}
		
		public string ProxyUserName
		{
			get
			{
				return proxyUserName;
			}
			set
			{
				proxyUserName = value;
			}
		}
		
		public string ProxyPassword
		{
			get
			{
				return proxyPassword;
			}
			set
			{
				proxyPassword = value;
			}
		}
	}
	
	public interface IJsonPluggableLibrary
	{
        bool IsArrayCompatible(string jsonString);

        bool IsDictionaryCompatible(string jsonString);

        string SerializeToJsonString(object objectToSerialize);
		
		List<object> DeserializeToListOfObject(string jsonString);
		
		object DeserializeToObject(string jsonString);

        //T DeserializeToObject<T>(string jsonString);
		
		Dictionary<string, object> DeserializeToDictionaryOfObject(string jsonString);
	}


    public enum PubnubErrorSeverity
    {
        Info,
        Warn,
        Critical
    }

    public enum PubnubMessageSource
    {
        Server,
        Client
    }

    public class PubnubClientError
    {
        int _statusCode;
        PubnubErrorSeverity _errorSeverity;
        bool _isDotNetException;
        PubnubMessageSource _messageSource;
        string _message = "";
        string _channel;
        Exception _detailedDotNetException = null;
        PubnubWebRequest _pubnubWebRequest = null;
        PubnubWebResponse _pubnubWebResponse = null;
        string _description = "";
        DateTime _dateTimeGMT;

        public PubnubClientError()
        {
        }

        public PubnubClientError(int statusCode, PubnubErrorSeverity errorSeverity, bool isDotNetException, string message, Exception detailedDotNetException, PubnubMessageSource source, PubnubWebRequest pubnubWebRequest, PubnubWebResponse pubnubWebResponse, string description, string channel)
        {
            _dateTimeGMT = DateTime.Now.ToUniversalTime();
            _statusCode = statusCode;
            _isDotNetException = isDotNetException;
            _message = message;
            _errorSeverity = errorSeverity;
            _messageSource = source;
            _channel = channel;
            _detailedDotNetException = detailedDotNetException;
            _pubnubWebRequest = pubnubWebRequest;
            _pubnubWebResponse = pubnubWebResponse;
            _description = description;
        }

        //public PubnubClientError(int statusCode, PubnubErrorSeverity errorSeverity, string message, PubnubMessageSource source, string channel)
        //{
        //    _dateTimeGMT = DateTime.Now.ToUniversalTime();
        //    _statusCode = statusCode;
        //    _isDotNetException = false;
        //    _message = message;
        //    _errorSeverity = errorSeverity;
        //    _messageSource = source;
        //    _channel = channel;
        //    _detailedDotNetException = null;
        //    //_description = PubnubErrorCodeDescription.GetStatusCodeDescription(statusCode, message);
        //}

        public PubnubClientError(int statusCode, PubnubErrorSeverity errorSeverity, string message, PubnubMessageSource source, PubnubWebRequest pubnubWebRequest, PubnubWebResponse pubnubWebResponse, string description, string channel)
        {
            _dateTimeGMT = DateTime.Now.ToUniversalTime();
            _statusCode = statusCode;
            _isDotNetException = false;
            _message = message;
            _errorSeverity = errorSeverity;
            _messageSource = source;
            _channel = channel;
            _detailedDotNetException = null;
            _pubnubWebRequest = pubnubWebRequest;
            _pubnubWebResponse = pubnubWebResponse;
            _description = description;
        }

        public int StatusCode
        {
            get
            {
                return _statusCode;
            }
        }

        public PubnubErrorSeverity Category
        {
            get
            {
                return _errorSeverity;
            }
        }

        public PubnubMessageSource MessageSource
        {
            get
            {
                return _messageSource;
            }
        }

        public bool IsDotNetException
        {
            get
            {
                return _isDotNetException;
            }
        }

        public string Message
        {
            get
            {
                return _message;
            }
        }

        public Exception DetailedDotNetException
        {
            get
            {
                return _detailedDotNetException;
            }
        }

        public PubnubWebRequest PubnubWebRequest
        {
            get
            {
                return _pubnubWebRequest;
            }
        }

        public PubnubWebResponse PubnubWebResponse
        {
            get
            {
                return _pubnubWebResponse;
            }
        }

        public string Channel
        {
            get
            {
                return _channel;
            }
        }

        public string Description
        {
            get
            {
                return _description;
            }
        }

        public DateTime ErrorDateTimeGMT()
        {
            return _dateTimeGMT;
        }

        public override string ToString()
        {
            StringBuilder errorBuilder= new StringBuilder();
            errorBuilder.AppendFormat("StatusCode={0} ", _statusCode);
            errorBuilder.AppendLine();
            errorBuilder.AppendFormat("Category={0} ", _errorSeverity.ToString());
            errorBuilder.AppendLine();
            errorBuilder.AppendFormat("MessageSource={0} ", _messageSource.ToString());
            errorBuilder.AppendLine();
            errorBuilder.AppendFormat("IsDotNetException={0} ", _isDotNetException.ToString());
            errorBuilder.AppendLine();
            errorBuilder.AppendFormat("Message={0} ", _message);
            errorBuilder.AppendLine();
            errorBuilder.AppendFormat("DetailedDotNetException={0} ", (_detailedDotNetException != null) ? _detailedDotNetException.ToString() : "");
            errorBuilder.AppendLine();
            errorBuilder.AppendFormat("PubnubWebRequest={0} ", (_pubnubWebRequest != null) ? _pubnubWebRequest.ToString() : "");
            errorBuilder.AppendLine();
            errorBuilder.AppendFormat("PubnubWebResponse={0} ", (_pubnubWebResponse != null) ? _pubnubWebResponse.ToString() : "");
            errorBuilder.AppendLine();
            errorBuilder.AppendFormat("Channel={0} ", _channel);
            errorBuilder.AppendLine();
            errorBuilder.AppendFormat("Description={0} ", _description);
            errorBuilder.AppendLine();
            errorBuilder.AppendFormat("ErrorDateTimeGMT={0} ", _dateTimeGMT);
            errorBuilder.AppendLine();

            return errorBuilder.ToString();
        }
    }

    internal static class PubnubErrorCodeHelper
    {

        public static PubnubErrorCode GetErrorType(WebExceptionStatus webExceptionStatus)
        {
            PubnubErrorCode ret = PubnubErrorCode.None;
            switch (webExceptionStatus)
            {
                case WebExceptionStatus.NameResolutionFailure:
                    ret = PubnubErrorCode.NameResolutionFailure;
                    break;
                case WebExceptionStatus.RequestCanceled:
                    ret = PubnubErrorCode.WebRequestCanceled;
                    break;
                case WebExceptionStatus.ConnectFailure:
                    ret = PubnubErrorCode.ConnectFailure;
                    break;
                case WebExceptionStatus.ProtocolError:
                    ret = PubnubErrorCode.ProtocolError;
                    break;
                case WebExceptionStatus.ServerProtocolViolation:
                    ret = PubnubErrorCode.ServerProtocolViolation;
                    break;
                default:
                    Console.WriteLine("ATTENTION: webExceptionStatus = " + webExceptionStatus.ToString());
                    break;
            }
            return ret;
        }

        public static PubnubErrorCode GetErrorType(Exception ex)
        {
            PubnubErrorCode ret = PubnubErrorCode.None;

            string errorType = ex.GetType().ToString();
            string errorMessage = ex.Message;

            if (errorType == "System.FormatException" && errorMessage == "Invalid length for a Base-64 char array or string.")
            {
                ret = PubnubErrorCode.PubnubMessageDecryptException;
            }
            else if (errorType == "System.FormatException" && errorMessage == "The input is not a valid Base-64 string as it contains a non-base 64 character, more than two padding characters, or an illegal character among the padding characters. ")
            {
                ret = PubnubErrorCode.PubnubMessageDecryptException;
            }
            else if (errorType == "System.ObjectDisposedException" && errorMessage == "Cannot access a disposed object.")
            {
                ret = PubnubErrorCode.PubnubObjectDisposedException;
            }
            else if (errorType == "System.Net.Sockets.SocketException" && errorMessage == "The requested name is valid, but no data of the requested type was found")
            {
                ret = PubnubErrorCode.PubnubSocketConnectException;
            }
            else if (errorType == "System.Net.Sockets.SocketException" && errorMessage == "No such host is known")
            {
                ret = PubnubErrorCode.PubnubSocketConnectException;
            }
            else if (errorType == "System.Security.Cryptography.CryptographicException" && errorMessage == "Padding is invalid and cannot be removed.")
            {
                ret = PubnubErrorCode.PubnubCryptographicException;
            }
            else
            {
                Console.WriteLine("ATTENTION: Error Type = " + errorType);
                Console.WriteLine("ATTENTION: Error Message = " + errorMessage);
                ret = PubnubErrorCode.None;
            }
            return ret;
        }

        public static PubnubErrorCode GetErrorType(int statusCode, string httpErrorCodeMessage)
        {
            PubnubErrorCode ret = PubnubErrorCode.None;

            switch (statusCode)
            {
                case 400:
                    if (httpErrorCodeMessage.ToUpper() == "MESSAGE TOO LARGE")
                    {
                        ret = PubnubErrorCode.MessageTooLarge;
                    }
                    else if (httpErrorCodeMessage.ToUpper() == "BADREQUEST")
                    {
                        ret = PubnubErrorCode.BadRequest;
                    }
                    break;
                case 401:
                    ret = PubnubErrorCode.InvalidSubscribeKey;
                    break;
                case 403:
                    if (httpErrorCodeMessage.ToUpper() == "FORBIDDEN")
                    {
                        ret = PubnubErrorCode.Forbidden;
                    }
                    else if (httpErrorCodeMessage.ToUpper() == "SIGNATURE DOES NOT MATCH")
                    {
                        ret = PubnubErrorCode.SignatureDoesNotMatch;
                    }
                    break;
                case 414:
                    ret = PubnubErrorCode.RequestUriTooLong;
                    break;
                case 500:
                    ret = PubnubErrorCode.InternalServerError;
                    break;
                case 502:
                    ret = PubnubErrorCode.BadGateway;
                    break;
                case 504:
                    ret = PubnubErrorCode.GatewayTimeout;
                    break;
                default:
                    break;
            }

            return ret;
        }

    }

    internal enum PubnubErrorCode
    {
        //www.iana.org/assignments/http-status-codes/http-status-codes.xhtml
        None = 0,
        NameResolutionFailure = 103,
        PubnubMessageDecryptException = 104,
        WebRequestCanceled = 105,
        ConnectFailure = 106,
        PubnubObjectDisposedException = 107,
        PubnubSocketConnectException = 108,
        NoInternet = 109,
        YesInternet = 110,
        DuplicateChannel = 111,
        AlreadySubscribed = 112,
        AlreadyPresenceSubscribed = 113,
        PubnubCryptographicException = 114,
        ProtocolError = 115,
        ServerProtocolViolation = 116,
        MessageTooLarge = 4000,
        BadRequest = 4001,
        InvalidSubscribeKey = 4010,
        Forbidden = 4030,
        SignatureDoesNotMatch = 4031,
        RequestUriTooLong = 4140,
        InternalServerError = 5000,
        BadGateway = 5020,
        GatewayTimeout = 5040
    }

    internal static class PubnubErrorCodeDescription
    {
        private static Dictionary<int, string> dictionaryCodes = new Dictionary<int, string>();

        static PubnubErrorCodeDescription()
        {
            //HTTP ERROR CODES and PubNub Context description
            dictionaryCodes.Add(4000, "For Publish, 1800 characters is the message size after we urlencode including http and host name. If you need to send larger messages, which may be >1.8KB after urlencoding, you can enable elastic message size in the admin to support this. For pricing information, please contact PubNub support");
            dictionaryCodes.Add(4001, "Bad Request. Please check the entered inputs or web request URL");
            dictionaryCodes.Add(4010, "Please provide correct subscribe key");
            dictionaryCodes.Add(4030, "Not authorized to access. Please ensure that the channel has permissions using PAM. Please check your authentication key value also for access. For assistance on PAM credentials, please contact PubNub support. Once the permission issue is resolved, Unsubscribe/Presence-Unsubscribe the channel and subscribe/presence again accordingly.");
            dictionaryCodes.Add(4031, "Please verify publish key and secret key. For assistance, contact PubNub support");
            dictionaryCodes.Add(4140, "URL request too long. Reduce the length by reducing subscription/presence channels or grant/revoke/audit channels/auth key list");
            dictionaryCodes.Add(5000, "Internal Server Error. Unexpected error occured at PubNub Server. Please try again. If same problem persists, please contact PubNub support");
            dictionaryCodes.Add(5020, "Bad Gateway. Unexpected error occured at PubNub Server. Please try again. If same problem persists, please contact PubNub support");
            dictionaryCodes.Add(5040, "Gateway Timeout. No response from server due to PubNub server timeout. Please try again. If same problem persists, please contact PubNub support");

            //PubNub API ERROR CODES and PubNub Context description
            dictionaryCodes.Add(103, "Please verify origin host name and internet connectivity");
            dictionaryCodes.Add(104, "Please verify your cipher key");
            dictionaryCodes.Add(105, "Web Request was cancelled due to change in subsciber/presence channel list or cancelled for object cleaning at the end of Pubnub object session");
            dictionaryCodes.Add(106, "Please check network/internet connection");
            dictionaryCodes.Add(107, "Internal exception. Please ignore"); //This won't go to callback. It will be suppressed.
            dictionaryCodes.Add(108, "Please check network/internet connection");
            dictionaryCodes.Add(109, "No network/internet connection. Please check network/internet connection");
            dictionaryCodes.Add(110, "Network/internet connection is back. Active subscriber/presence channels will be restored.");
            dictionaryCodes.Add(111, "Duplicate channel subscription is not allowed. Internally Pubnub API removes the duplicates before processing");
            dictionaryCodes.Add(112, "Channel Already Subscribed. Duplicate channel subscription not allowed");
            dictionaryCodes.Add(113, "Channel Already Presence-Subscribed. Duplicate channel presence-subscription not allowed");
            dictionaryCodes.Add(114, "Please verify your cipher key");
            dictionaryCodes.Add(115, "Protocol Error. Please contact PubNub with error details.");
            dictionaryCodes.Add(116, "ServerProtocolViolation. Please contact PubNub with error details.");
            dictionaryCodes.Add(0, "**** NEED INVESTIGATION *****");
        }

        public static string GetStatusCodeDescription(PubnubErrorCode pubnubErrorCode)
        {
            string defaultDescription = "Please contact PubNub support with your error object details";
            int key = (int)pubnubErrorCode;
            string description = dictionaryCodes.ContainsKey(key) ? dictionaryCodes[key] : defaultDescription;
            return description;
        }
    }

#if (USE_JSONFX)
	public class JsonFXDotNet : IJsonPluggableLibrary
	{
		public string SerializeToJsonString(object objectToSerialize)
		{
            string json = "";
            var resolver = new CombinedResolverStrategy(new DataContractResolverStrategy());
            DataWriterSettings dataWriterSettings = new DataWriterSettings(resolver);
            var writer = new JsonFx.Json.JsonWriter(dataWriterSettings, new string[] { "PubnubClientError" });
            json = writer.Write(objectToSerialize);
            return json;
		}

		public List<object> DeserializeToListOfObject(string jsonString)
		{
			var reader = new JsonFx.Json.JsonReader();
			var output = reader.Read<List<object>>(jsonString);
			return output;
		}
		
		public object DeserializeToObject(string jsonString)
		{
			var reader = new JsonFx.Json.JsonReader();
			var output = reader.Read<object>(jsonString);
			return output;
		}
		
		public Dictionary<string, object> DeserializeToDictionaryOfObject(string jsonString)
		{
			var reader = new JsonFx.Json.JsonReader();
			var output = reader.Read<Dictionary<string, object>>(jsonString);
			return output;
		}

        //public T DeserializeToObject<T>(string jsonString)
        //{
        //    T result = default(T);
        //    JsonReader reader;
        //    PubnubClientError clientError = null;
        //    if (typeof(T) == typeof(PubnubClientError))
        //    {
        //        reader = new JsonFx.Json.JsonReader();
        //        Dictionary<string, object> pubnubErrorMessage = reader.Read(jsonString) as Dictionary<string, object>;
        //        if (pubnubErrorMessage != null)
        //        {
        //            int httpStatusCode = (int)pubnubErrorMessage["HttpStatusCode"];
        //            PubnubMessageSource source = (PubnubMessageSource)Enum.Parse(typeof(PubnubMessageSource), pubnubErrorMessage["MessageSource"].ToString());
        //            PubnubErrorCategory category = (PubnubErrorCategory)Enum.Parse(typeof(PubnubErrorCategory), pubnubErrorMessage["Category"].ToString());
        //            bool isDotNetException = Boolean.Parse(pubnubErrorMessage["IsDotNetException"].ToString());
        //            string message = pubnubErrorMessage["Message"].ToString();
        //            string detailedDotNetException = pubnubErrorMessage["DetailedDotNetException"].ToString();
        //            string channel = pubnubErrorMessage["Channel"].ToString();

        //            clientError = new PubnubClientError(httpStatusCode, category, isDotNetException, message, detailedDotNetException, source, channel);
        //            result = (T)Convert.ChangeType(clientError, typeof(T));
        //        }

        //    }
        //    else if (typeof(T) == typeof(object))
        //    {
        //        reader = new JsonFx.Json.JsonReader();
        //        object deserializedMessage = reader.Read<object>(jsonString);
        //        result = (T)deserializedMessage;
        //    }
        //    return result;
        //}
    }
#elif (USE_DOTNET_SERIALIZATION)
	public class JscriptSerializer : IJsonPluggableLibrary
	{
		public string SerializeToJsonString(object objectToSerialize)
		{
			JavaScriptSerializer jS = new JavaScriptSerializer();
			return jS.Serialize(objectToSerialize);
		}
		
		public List<object> DeserializeToListOfObject(string jsonString)
		{
			JavaScriptSerializer jS = new JavaScriptSerializer();
			return (List<object>)jS.Deserialize<List<object>>(jsonString);
		}
		
		public object DeserializeToObject(string jsonString)
		{
			JavaScriptSerializer jS = new JavaScriptSerializer();
			return (object)jS.Deserialize<object>(jsonString);
		}
		
		public Dictionary<string, object> DeserializeToDictionaryOfObject(string jsonString)
		{
			JavaScriptSerializer jS = new JavaScriptSerializer();
			return (Dictionary<string, object>)jS.Deserialize<Dictionary<string, object>>(jsonString);
		}

        //public T DeserializeToObject<T>(string jsonString)
        //{
        //    T result = default(T);

        //    PubnubClientError clientError = null;
        //    JavaScriptSerializer jS = new JavaScriptSerializer();

        //    if (typeof(T) == typeof(PubnubClientError))
        //    {
        //        Dictionary<string, object> pubnubErrorMessage = jS.Deserialize<Dictionary<string, object>>(jsonString);
        //        if (pubnubErrorMessage != null)
        //        {
        //            PubnubMessageSource source = (PubnubMessageSource)Enum.Parse(typeof(PubnubMessageSource), pubnubErrorMessage["MessageSource"].ToString());
        //            PubnubErrorCategory category = (PubnubErrorCategory)Enum.Parse(typeof(PubnubErrorCategory), pubnubErrorMessage["Category"].ToString());
        //            bool isDotNetException = Boolean.Parse(pubnubErrorMessage["IsDotNetException"].ToString());
        //            string message = pubnubErrorMessage["Message"].ToString();
        //            string detailedDotNetException = pubnubErrorMessage["DetailedDotNetException"].ToString();
        //            string channel = pubnubErrorMessage["Channel"].ToString();

        //            clientError = new PubnubClientError(category, isDotNetException, message, detailedDotNetException, source, channel);
        //            result = (T)Convert.ChangeType(clientError, typeof(T));
        //        }
        //    }
        //    else if (typeof(T) == typeof(object))
        //    {
        //        object deserializedResult = jS.Deserialize<object>(jsonString);
        //        result = (T)deserializedResult;
        //    }
        //    return result;
        //}
	}
#else
	public class NewtonsoftJsonDotNet : IJsonPluggableLibrary
	{
		#region IJsonPlugableLibrary methods implementation

        public bool IsArrayCompatible(string jsonString)
        {
            bool ret = false;
            JsonTextReader reader = new JsonTextReader(new StringReader(jsonString));
            while (reader.Read())
            {
                if (reader.LineNumber == 1 && reader.LinePosition == 1 && reader.TokenType == JsonToken.StartArray)
                {
                    ret = true;
                    break;
                }
                else
                {
                    break;
                }
            }

            return ret;
        }

        public bool IsDictionaryCompatible(string jsonString)
        {
            bool ret = false;
            JsonTextReader reader = new JsonTextReader(new StringReader(jsonString));
            while (reader.Read())
            {
                if (reader.LineNumber == 1 && reader.LinePosition == 1 && reader.TokenType == JsonToken.StartObject)
                {
                    ret = true;
                    break;
                }
                else
                {
                    break;
                }
            }

            return ret;
        }
        
        public string SerializeToJsonString(object objectToSerialize)
		{
            return JsonConvert.SerializeObject(objectToSerialize);
		}
		
		public List<object> DeserializeToListOfObject(string jsonString)
		{
			List<object> result = JsonConvert.DeserializeObject<List<object>>(jsonString);
			
			//var resultItems = (from item in result
			//                select item as object).ToArray();
			
			//result = resultItems.ToList();
			
			return result;
		}
		
		public object DeserializeToObject(string jsonString)
		{
			object result = JsonConvert.DeserializeObject<object>(jsonString);
			if (result.GetType().ToString() == "Newtonsoft.Json.Linq.JArray")
			{
				JArray jarrayResult = result as JArray;
				List<object> objectContainer = jarrayResult.ToObject<List<object>>();
				if (objectContainer != null && objectContainer.Count > 0)
				{
					for (int index = 0; index < objectContainer.Count; index++)
					{
						if (objectContainer[index].GetType().ToString() == "Newtonsoft.Json.Linq.JArray")
						{
							JArray internalItem = objectContainer[index] as JArray;
							objectContainer[index] = internalItem.Select(item => (object)item).ToArray();
						}
					}
					result = objectContainer;
				}
			}
			return result;
		}

        //public T DeserializeToObject<T>(string jsonString)
        //{
        //    T result = default(T);
        //    object deserializedMessage = JsonConvert.DeserializeObject<object>(jsonString, new JsonSerializerSettings { Formatting = Newtonsoft.Json.Formatting.None });
        //    if (deserializedMessage != null)
        //    {
        //        if (typeof(T) == typeof(PubnubClientError) && (deserializedMessage.GetType().ToString() == "Newtonsoft.Json.Linq.JObject"))
        //        {
        //            JObject errorObject = deserializedMessage as JObject;
        //            int httpStatusCode = errorObject.Value<int>("HttpStatusCode");
        //            PubnubMessageSource source = (PubnubMessageSource)Enum.Parse(typeof(PubnubMessageSource), errorObject["MessageSource"].ToString());
        //            PubnubErrorCategory category = (PubnubErrorCategory)Enum.Parse(typeof(PubnubErrorCategory), errorObject["Category"].ToString());
        //            bool isDotNetException = Boolean.Parse(errorObject["IsDotNetException"].ToString());
        //            string message = errorObject["Message"].ToString();
        //            string detailedDotNetException = errorObject["DetailedDotNetException"].ToString();
        //            string channel = errorObject["Channel"].ToString();
        //            PubnubClientError clientError;
        //            clientError = new PubnubClientError(httpStatusCode, category, isDotNetException, message, detailedDotNetException, source, channel);
        //            result = (T)Convert.ChangeType(clientError, typeof(T));
        //        }
        //        else if (typeof(T) == typeof(object))
        //        {
        //            result = (T)deserializedMessage;
        //        }
        //    }
        //    return result;
        //}
		
		public Dictionary<string, object> DeserializeToDictionaryOfObject(string jsonString)
		{
			return JsonConvert.DeserializeObject<Dictionary<string, object>>(jsonString);
		}
#endregion
	}
#endif
	
#if (__MonoCS__)
	class StateObject<T>
	{
		public RequestState<T> RequestState
		{
			get;
			set;
		}
		
		public TcpClient tcpClient = null;
		public NetworkStream netStream = null;
		public SslStream sslns = null;
		public const int BufferSize = 2048;
		public byte[] buffer = new byte[BufferSize];
		public StringBuilder sb = new StringBuilder();
		public string requestString = null;
	}
#endif
}