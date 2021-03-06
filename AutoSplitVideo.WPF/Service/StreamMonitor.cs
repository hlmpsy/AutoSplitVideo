﻿using AutoSplitVideo.Event;
using AutoSplitVideo.Model;
using BilibiliApi;
using BilibiliApi.Enum;
using BilibiliApi.Event;
using BilibiliApi.Model;
using System;
using System.Threading.Tasks;
using Timer = System.Timers.Timer;

namespace AutoSplitVideo.Service
{
	public sealed class StreamMonitor : IDisposable
	{
		private int RoomId { get; }
		public event RoomInfoUpdatedEvent RoomInfoUpdated;
		public event StreamStartedEvent StreamStarted;
		public event LogEvent LogEvent;

		private readonly Timer _httpTimer;
		private readonly DanMuClient _danMuClient;

		public StreamMonitor(RoomSetting setting)
		{
			RoomId = setting.RoomId;
#if DEBUG
			StreamStarted += (o, args) =>
			{
				LogEvent?.Invoke(this,
						args.IsLive
								? new LogEventArgs { Log = $@"[{RoomId}] [{args.Type}] [{setting.UserName}] 开播：{setting.Title}" }
								: new LogEventArgs { Log = $@"[{RoomId}] [{args.Type}] [{setting.UserName}] 下播/未开播" });
			};
#endif
			_danMuClient = new DanMuClient(RoomId, TimeSpan.FromMilliseconds(setting.TimingDanmakuRetry));
			_danMuClient.LogEvent += (o, args) => LogEvent?.Invoke(o, args);
			_danMuClient.ReceivedDanmaku += (o, args) =>
			{
				switch (args.Danmaku.MsgType)
				{
					case MsgType.LiveStart:
						StreamStarted?.Invoke(this, new StreamStartedArgs
						{
							Type = TriggerType.弹幕,
							IsLive = true
						});
						break;
					case MsgType.LiveEnd:
						StreamStarted?.Invoke(this, new StreamStartedArgs
						{
							Type = TriggerType.弹幕,
							IsLive = false
						});
						break;
				}
			};
			_httpTimer = new Timer(TimeSpan.FromSeconds(setting.TimingCheckInterval).TotalMilliseconds)
			{
				Enabled = false,
				AutoReset = true,
				SynchronizingObject = null,
				Site = null
			};
			_httpTimer.Elapsed += (sender, e) =>
			{
				Check(TriggerType.HttpApi);
			};
		}

		public bool Start()
		{
			if (_disposedValue)
			{
				throw new ObjectDisposedException(nameof(StreamMonitor));
			}

			_danMuClient.Start();
			_httpTimer.Start();
			Check(TriggerType.HttpApiFirstCheck);
			return true;
		}

		public void Stop()
		{
			if (_disposedValue)
			{
				throw new ObjectDisposedException(nameof(StreamMonitor));
			}

			_httpTimer.Stop();
		}

		public async void Check(TriggerType type, int millisecondsDelay = 0)
		{
			if (_disposedValue)
			{
				throw new ObjectDisposedException(nameof(StreamMonitor));
			}

			if (millisecondsDelay < 0)
			{
				throw new ArgumentOutOfRangeException(nameof(millisecondsDelay), @"不能小于0");
			}

			try
			{
				await Task.Delay(millisecondsDelay);
				StreamStarted?.Invoke(this, new StreamStartedArgs { Type = type, IsLive = (await FetchRoomInfoAsync()).IsStreaming });
			}
			catch (Exception ex)
			{
				LogEvent?.Invoke(this, new LogEventArgs { Log = $@"[{RoomId}] 获取直播间开播状态出错：{ex.Message}" });
			}
		}

		private async Task<Room> FetchRoomInfoAsync()
		{
			var room = await BililiveApi.GetRoomInfoAsync(RoomId);
			RoomInfoUpdated?.Invoke(this, new RoomInfoUpdatedArgs { Room = room });
			return room;
		}

		public void SettingChanged(RoomSetting setting)
		{
			if (_disposedValue)
			{
				throw new ObjectDisposedException(nameof(StreamMonitor));
			}

			_httpTimer.Interval = TimeSpan.FromSeconds(setting.TimingCheckInterval).TotalMilliseconds;
			_danMuClient.SetTimingDanmakuRetry(TimeSpan.FromMilliseconds(setting.TimingDanmakuRetry));
		}

		#region IDisposable Support
		private bool _disposedValue; // 要检测冗余调用

		private void Dispose(bool disposing)
		{
			if (!_disposedValue)
			{
				if (disposing)
				{
					_httpTimer?.Dispose();
					_danMuClient?.Dispose();
					LogEvent?.Invoke(this, new LogEventArgs { Log = $@"[{RoomId}] 弹幕连接已断开" });
				}

				RoomInfoUpdated = null;
				StreamStarted = null;
				LogEvent = null;

				_disposedValue = true;
			}
		}

		public void Dispose()
		{
			Dispose(true);
		}
		#endregion
	}
}