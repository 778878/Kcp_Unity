using System;
using System.Net;
using System.Net.Sockets;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Kcp.Event;
using Kcp.Interface;
using Kcp.Logger;
using Kcp.SegmentPool;
using Kcp.Setting;
using Kcp.Timer;
using Kcp.Utility;
using Debug = UnityEngine.Debug;


#nullable enable

namespace Kcp
{
    public sealed class Kcp :
        IDisposable, IExternalSetting, IKcpUpdate, IKcpInputAble, IKcpSendAble
    {
        private const int MinimumNoDelayRto = 30; // 无延迟最小 RTO
        private const int MinimumRto = 100; // 正常最小 RTO
        private const int DefaultRto = 200; // 默认 RTO
        private const int MaximumRto = 3000; // 最大 RTO
        private const int TimeOutRto = 60000; // 超时 RTO
        private const int ProbeRto = 30000; // 探测RTO
        private const ushort MaxWindowSize = 65535; // 最大窗口限制
        private const int CommandPush = 80; // 命令：推送数据
        private const int CommandPushFast = 81; // 命令：推送数据(快速)
        private const int CommandAck = 82; // 命令：确认
        private const int CommandProbeInquire = 83; // 命令：窗口探测（询问）
        private const int CommandProbeInform = 84; // 命令：窗口探测（告知）

        private const int AskSend = 1; // 需要发送 CommandProbeInquire
        private const int AskTell = 2; // 需要发送 CommandProbeInform
        private const int DefaultMtu = 1400; // 默认 MTU
        private const int FastAck = 3; // 快速确认
        private const int Overhead = 14; // 头部开销
        private const int AckOverhead = 11; // ACK头部开销
        private const int ProbeOverhead = 5; // Probe头部开销
        private const uint UpdateInterval = 10; // 更新间隔
        private const int ProbeInterval = 5000; // 探测包发送间隔（5 秒）
        

        private uint ConversationId; // 会话编号
        private uint MaximumTransferUnit = DefaultMtu; // 最大传输单元
        private uint MaximumSegmentSize; // 最大报文段大小
        private uint State; // 状态
        private uint SendUnacknowledgedNumber; // 已发送但未确认的序列号
        private int SendNumber; // 下一个发送的包
        private int ReceiveNumber; // 下一个接收的包
        private ushort SendWindow = 32; // 发送窗口大小
        private ushort ReceiveWindow = 128; // 接收窗口大小
        private ulong RecentTimestamp; // 最近的时间戳
        private ulong LastAckTimestamp; // 最后确认的时间戳
        private uint CongestionThreshold; // 拥塞窗口阈值
        private ushort CongestionWindow; // 拥塞窗口大小
        private ulong RoundTripTime; // 往返时间
        private ulong RoundTripRecordTime; // 往返发出时间
        private ulong RoundTripTimeSequenceNumber; // 往返记录包
        private ulong SmoothRoundTripTime; // 平滑往返时间
        private ulong RoundTripTimeVariance; // 往返时间方差
        private ulong ReceiveRoundTripTimeout; // 重传超时时间
        private uint MinimumRoundTripTimeout; // 最小重传超时时间
        private uint RemoteWindow; // 远端窗口大小
        private uint ProbeFlag; // 探测标志
        private ulong CurrentTimestamp; // 当前时间戳
        private uint TransmitCount; // 发送计数
        private uint ReceiveBufferCount; // 接收缓冲区大小
        private uint SendBufferCount; // 发送缓冲区大小
        private uint ReceiveAckCount; // 接收确认计数
        private uint SendUnacknowledgedCount; // 未确认发送计数
        private uint SendWindowCount; // 发送窗口计数
        private uint ReceiveWindowCount; // 接收窗口计数
        private int ProbeCount; // 探测计数
        private float CongestionAddition; // 拥塞避免累积器
        private int CongestionLastLossCount;  // 记录上次触发减半时的丢包数量
        private bool CongestionRecoveryPhase; // 标记是否处于恢复期
        

        //Setting
        private bool IsDelayAck { get; set; }

        private int AckInterval { get; set; } = 50;
        
        private bool IsServer { get; set; }
        
        private bool IsClient { get; set; }
        
        private uint DeadProbeCount { get; set; } = 5; // 最大探测次数
        
        private uint DeadLinkCount { get; set; } = 5; // 最大重传次数

        private uint RetransmitThreshold { get; set; } = 5; //快速重传阈值

        private uint RttSamplingInterval { get; set; } = 20; //RTT采样频率

        //集合
        private ConcurrentQueue<Segment> SendQueue = new ConcurrentQueue<Segment>(); // 发送队列
        private ConcurrentQueue<Segment> ReceiveQueue = new ConcurrentQueue<Segment>(); // 接收队列
        private ConcurrentDictionary<uint, Segment> SendBuffer = new ConcurrentDictionary<uint, Segment>(); // 发送缓冲区
        private System.Collections.Generic.SortedDictionary<uint, Segment> ReceiveBuffer = new SortedDictionary<uint, Segment>(); // 接收缓冲区
        private HashSet<uint> AckList = new HashSet<uint>(); // 确认列表
        private ConcurrentDictionary<uint, PacketLoss> PacketLoss = new ConcurrentDictionary<uint, PacketLoss>(); //丢包集合
        private List<KeyValuePair<uint, PacketLoss>> RemovePacketLoss = new List<KeyValuePair<uint, PacketLoss>>();

        //网络
        private UdpClient UdpClient; // UDP 客户端
        private IPEndPoint? ReceiveEndPoint; // 接收到的端点
        private IPEndPoint RemoteEndPoint; // 远端端点
        private readonly IPEndPoint LocalEndPoint;
        private readonly ArrayPool<byte> ArrayPool = ArrayPool<byte>.Shared; // 共享数组池
        private readonly MemoryPool<byte> MemoryPool = MemoryPool<byte>.Shared; // 共享内存池
        private SegmentPool<Segment> SegmentPool { get; set; } = new SegmentPool<Segment>(10); // Segment池
        private readonly SegmentPool<SegmentAck> SegmentAckPool = new SegmentPool<SegmentAck>(10); // SegmentAck池
        private readonly System.Timers.Timer _timer; //计时器
        
        //事件
        private event ConnectionEventHandler? OnConnected;
        private event ConnectionEventHandler? OnDisconnected;
        private event ConnectionEventHandler? OnError;
        private event DataEventHandler? OnDataReceived;
        private event DataEventHandler? OnDataSent;

        //工具
        private readonly ReaderWriterLockSlim _lock = new ReaderWriterLockSlim(); //线程安全
        private readonly TimerPool TimerPool = new TimerPool(10); //计时器池
        private readonly Stopwatch _probeStopwatch = new Stopwatch(); // 用于探测计时
        private KcpTrafficMonitorSetting? _trafficMonitorSetting; //流量监控
        private IKcpEncryptor? _encryptor; //加密算法
        private IKcpLogger? _logger;

        private bool _isFirstProbe = true;

        #region 构造方法
        
        public Kcp(uint conversationId, IPEndPoint remoteEndPoint)
        {
            this.ConversationId = conversationId; //初始化会话ID
            this.RemoteEndPoint = remoteEndPoint; //远端
            this.LocalEndPoint = new IPEndPoint(IPAddress.Any, 0);
            this.UdpClient = new UdpClient(LocalEndPoint); //依赖注入
            this.MaximumSegmentSize = this.MaximumTransferUnit - Overhead; //初始化最大报文长度
            this.ReceiveRoundTripTimeout = DefaultRto; //初始化超时重传时间
            this.MinimumRoundTripTimeout = MinimumRto; //初始化重传最小RTO
            this.CongestionThreshold = 32; //初始化拥塞阈值
            this.CongestionWindow = 1; //初始化拥塞窗口
            this.RecentTimestamp = GetCurrentULongTimestamp(); //初始化时间戳
            
            SetMode(false, true); // 默认客户端行为

            _logger = _logger ?? new ConsoleKcpLogger(); //初始化日志
            
            _timer = new System.Timers.Timer(UpdateInterval); //初始化计时器
            _timer.Elapsed += async (_, _) => { await Update(GetCurrentULongTimestamp()); };
            _timer.Start(); // 启动定时器

            UdpClient.BeginReceive(Input, null);
            
            // 触发连接建立事件
            OnConnected?.Invoke(this, new ConnectionEventArgs(RemoteEndPoint, "Connection established"));
        }

        public Kcp(uint conversationId, IPEndPoint localEndPoint, IPEndPoint remoteEndPoint)
        {
            this.ConversationId = conversationId; //初始化会话ID
            this.RemoteEndPoint = remoteEndPoint; //远端
            this.LocalEndPoint = localEndPoint;
            this.UdpClient = new UdpClient(LocalEndPoint); //依赖注入
            this.MaximumSegmentSize = this.MaximumTransferUnit - Overhead; //初始化最大报文长度
            this.ReceiveRoundTripTimeout = DefaultRto; //初始化超时重传时间
            this.MinimumRoundTripTimeout = MinimumRto; //初始化重传最小RTO
            this.CongestionThreshold = 32; //初始化拥塞阈值
            this.CongestionWindow = 1; //初始化拥塞窗口
            this.RecentTimestamp = GetCurrentULongTimestamp(); //初始化时间戳
            
            SetMode(false, true); // 默认客户端行为
            
            _logger = _logger ?? new ConsoleKcpLogger(); //初始化日志

            _timer = new System.Timers.Timer(UpdateInterval); //初始化计时器
            _timer.Elapsed += async (_, _) => { await Update(GetCurrentULongTimestamp()); };
            _timer.Start(); // 启动定时器

            UdpClient.BeginReceive(Input, null);
            
            // 触发连接建立事件
            OnConnected?.Invoke(this, new ConnectionEventArgs(RemoteEndPoint, "Connection established"));
        }
        
        public Kcp(uint conversationId, IPEndPoint localEndPoint, IPEndPoint remoteEndPoint
        , KcpBehaviourSetting kcpBehaviourSetting)
        {
            this.ConversationId = conversationId; //初始化会话ID
            this.RemoteEndPoint = remoteEndPoint; //远端
            this.LocalEndPoint = localEndPoint;
            this.UdpClient = new UdpClient(LocalEndPoint); //依赖注入
            this.MaximumSegmentSize = this.MaximumTransferUnit - Overhead; //初始化最大报文长度
            this.ReceiveRoundTripTimeout = DefaultRto; //初始化超时重传时间
            this.MinimumRoundTripTimeout = MinimumRto; //初始化重传最小RTO
            this.CongestionThreshold = 32; //初始化拥塞阈值
            this.CongestionWindow = 1; //初始化拥塞窗口
            this.RecentTimestamp = GetCurrentULongTimestamp(); //初始化时间戳
            
            SetMode(kcpBehaviourSetting.IsServer, kcpBehaviourSetting.IsClient); // 默认客户端行为
            
            _logger = _logger ?? new ConsoleKcpLogger(); //初始化日志

            _timer = new System.Timers.Timer(UpdateInterval); //初始化计时器
            _timer.Elapsed += async (_, _) => { await Update(GetCurrentULongTimestamp()); };
            _timer.Start(); // 启动定时器

            UdpClient.BeginReceive(Input, null);
            
            // 触发连接建立事件
            OnConnected?.Invoke(this, new ConnectionEventArgs(RemoteEndPoint, "Connection established"));
        }
        
        public Kcp(uint conversationId, IPEndPoint localEndPoint, IPEndPoint remoteEndPoint
            , KcpDeadLinkSetting kcpDeadLinkSetting)
        {
            this.ConversationId = conversationId; //初始化会话ID
            this.RemoteEndPoint = remoteEndPoint; //远端
            this.LocalEndPoint = localEndPoint;
            this.UdpClient = new UdpClient(LocalEndPoint); //依赖注入
            this.MaximumSegmentSize = this.MaximumTransferUnit - Overhead; //初始化最大报文长度
            this.ReceiveRoundTripTimeout = DefaultRto; //初始化超时重传时间
            this.MinimumRoundTripTimeout = MinimumRto; //初始化重传最小RTO
            this.CongestionThreshold = 32; //初始化拥塞阈值
            this.CongestionWindow = 1; //初始化拥塞窗口
            this.RecentTimestamp = GetCurrentULongTimestamp(); //初始化时间戳
            
            SetKcpDeadLink(kcpDeadLinkSetting); // 默认死亡Link,Probe次数
            
            _logger = _logger ?? new ConsoleKcpLogger(); //初始化日志

            _timer = new System.Timers.Timer(UpdateInterval); //初始化计时器
            _timer.Elapsed += async (_, _) => { await Update(GetCurrentULongTimestamp()); };
            _timer.Start(); // 启动定时器

            UdpClient.BeginReceive(Input, null);
            
            // 触发连接建立事件
            OnConnected?.Invoke(this, new ConnectionEventArgs(RemoteEndPoint, "Connection established"));
        }
        
        public Kcp(uint conversationId, IPEndPoint localEndPoint, IPEndPoint remoteEndPoint
            , KcpBehaviourSetting kcpBehaviourSetting, KcpDeadLinkSetting kcpDeadLinkSetting)
        {
            this.ConversationId = conversationId; //初始化会话ID
            this.RemoteEndPoint = remoteEndPoint; //远端
            this.LocalEndPoint = localEndPoint;
            this.UdpClient = new UdpClient(LocalEndPoint); //依赖注入
            this.MaximumSegmentSize = this.MaximumTransferUnit - Overhead; //初始化最大报文长度
            this.ReceiveRoundTripTimeout = DefaultRto; //初始化超时重传时间
            this.MinimumRoundTripTimeout = MinimumRto; //初始化重传最小RTO
            this.CongestionThreshold = 32; //初始化拥塞阈值
            this.CongestionWindow = 1; //初始化拥塞窗口
            this.RecentTimestamp = GetCurrentULongTimestamp(); //初始化时间戳
            
            SetMode(kcpBehaviourSetting.IsServer, kcpBehaviourSetting.IsClient); // 默认客户端行为
            SetKcpDeadLink(kcpDeadLinkSetting); // 默认死亡Link,Probe次数
            
            _logger = _logger ?? new ConsoleKcpLogger(); //初始化日志

            _timer = new System.Timers.Timer(UpdateInterval); //初始化计时器
            _timer.Elapsed += async (_, _) => { await Update(GetCurrentULongTimestamp()); };
            _timer.Start(); // 启动定时器

            UdpClient.BeginReceive(Input, null);
            
            // 触发连接建立事件
            OnConnected?.Invoke(this, new ConnectionEventArgs(RemoteEndPoint, "Connection established"));
        }
        
        #endregion

        /// <summary>
        /// 更新机制
        /// </summary>
        /// <param name="currentTick"></param>
        private async Task Update(ulong currentTick) => await ((IKcpUpdate)this).Update(currentTick);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        async Task IKcpUpdate.Update(ulong currentTick)
        {
            CurrentTimestamp = currentTick;

            await ReceiveAsync(); //处理接收数据

            await CheckTimeout(currentTick); //检测连接超时

            await CheckPacketLoss(currentTick); //处理多次丢包重传
            
            await CheckWindow(); //检测窗口(拥塞控制)

            await UpdateDataAsync(); //刷新数据(发送数据)
        }

        /// <summary>
        /// 检测客户端连接超时
        /// </summary>
        /// <param name="currentTick"></param>
        private async ValueTask CheckTimeout(ulong currentTick)
        {
            if (RecentTimestamp >= currentTick) return;

            //心跳机制
            if (currentTick - RecentTimestamp > ProbeRto && IsServer)
            {
                //探测连接
                if (_isFirstProbe)
                {
                    await CheckLinkState(); //检测Kcp连接状态, 发送探测包
                    _probeStopwatch.Start(); //启动探测计时
                    _isFirstProbe = false; //标记已经发送第一次探测包
                }
                else if (!_isFirstProbe && _probeStopwatch.ElapsedMilliseconds >= ProbeInterval)
                {
                    await CheckLinkState();
                    _probeStopwatch.Restart();
                }

            }
            else if (currentTick - RecentTimestamp > TimeOutRto || ProbeCount >= DeadProbeCount)
            {
                Dispose(); //断开连接
            }
        }

        /// <summary>
        /// 检测窗口，更新拥塞控制，滑动窗口等逻辑
        /// </summary>
        private async Task CheckWindow()
        {
            await CongestionControl();
        }

        /// <summary>
        /// 定时发送探测包
        /// </summary>
        private async Task CheckLinkState()
        {
            using var buffer = BinarySegmentProbe(PackSegmentAutoProbeInquire());

            try
            {
                
                ReadOnlyMemory<byte> dataToSend = buffer.Memory;

                // 加密数据
                if (_encryptor is not null)
                {
                    dataToSend = _encryptor.Encrypt(dataToSend);
                }
                
                await UdpClient.SendAsync(dataToSend.ToArray(), ProbeOverhead, RemoteEndPoint);
            }
            catch (Exception)
            {
                _logger?.LogError("探测包发送失败");
            }
            finally
            {
                Interlocked.Increment(ref ProbeCount);
                _logger?.LogInfo("发送探测包");
            }


        }

        /// <summary>
        /// 处理多次丢包重传
        /// </summary>
        /// <param name="currentTick"></param>
        private async ValueTask CheckPacketLoss(ulong currentTick)
        {
            if (PacketLoss.IsEmpty) return;

            await Task.Run(() =>
            {
                foreach (var packet in PacketLoss)
                {
                    if (packet.Value.LossType is LossType.快速包) 
                    {
                        
                        RemovePacketLoss.Add(packet); //进入移除队列
                        
                        continue; //快速包不做处理
                    }
                    
                    if (packet.Value.RetransmissionCount >= DeadLinkCount)
                    {
                        RemovePacketLoss.Add(packet); //进入移除队列
                        continue;
                    }

                    if (currentTick - packet.Value.LastRetransmissionTime < MinimumRoundTripTimeout) continue;

                    packet.Value.LastRetransmissionTime = currentTick;
                    packet.Value.RetransmissionCount += 1;
                    SendData(packet.Value.Segment!); //重传
                }
            });


            await RemoveRetransmission();
        }


        /// <summary>
        /// 打包数据并发送
        /// </summary>
        private async ValueTask UpdateDataAsync()
        {
            while (SendQueue.TryDequeue(out var seg))
            {
                if (SendNumber >= Math.Min(SendWindow, (int)CongestionWindow)) break; // 检查是否超出可用窗口


                using var buffer = BinarySegment(seg); // 序列化

                try
                {
                    Interlocked.Increment(ref SendNumber);

                    ReadOnlyMemory<byte> dataToSend = buffer.Memory;

                    // 加密数据
                    if (_encryptor is not null)
                    {
                        dataToSend = _encryptor.Encrypt(dataToSend);
                    }
                    
                    await UdpClient.SendAsync(dataToSend.ToArray(), seg.Data.Length + Overhead, RemoteEndPoint);
                    _logger?.LogInfo("发送成功！");
                }
                catch (Exception e)
                {
                    Console.WriteLine(e);
                    throw;
                }
                finally
                {
                    var sequenceNumber = seg.SequenceNumber;
                    
                    SendBuffer.TryAdd(sequenceNumber, seg); //存入发送缓存
                    
                    TimerPool.GetOneTimer(sequenceNumber, ReceiveRoundTripTimeout, () => //注册计时器
                    {
                        AutoRetransmission(sequenceNumber);
                    });
                    
                    OnDataSent?.Invoke(this, new DataEventArgs(seg.Data)); // 触发数据发送事件
                    
                    _trafficMonitorSetting?.AddSent(buffer.Memory.Length);

                    BeginAutoRetransmissionControl(seg.SequenceNumber); // 开始动态计算Rto
                }

            }
            Interlocked.Exchange(ref SendNumber, 0);
        }

        /// <summary>
        /// 内部接收数据，放入接收队列
        /// </summary>
        private async void Input(IAsyncResult asyncResult)
        {
            try
            {
                var data = UdpClient.EndReceive(asyncResult, ref ReceiveEndPoint!);
                
                ReadOnlyMemory<byte> dataToReceive = data.AsMemory();

                // 解密数据
                if (_encryptor is not null)
                {
                    dataToReceive = _encryptor.Decrypt(dataToReceive);
                }

                uint conversationId = BitConverter.ToUInt32(dataToReceive.Span[..4]); //检测会话ID

                await ProcessInputDataAsync(conversationId, dataToReceive);
                
                _trafficMonitorSetting?.AddReceived(data.Length);
            }
            catch (SocketException socketException)
            {
                _logger?.LogError("对方主机未连接");
                _logger?.LogError(socketException.Message);
                Dispose(); //断开连接
            }
            catch (ObjectDisposedException)
            {
                _logger?.LogWarning(nameof(UdpClient) + "已被释放");
                Dispose();
            }
            finally
            {
                try
                {
                    UdpClient.BeginReceive(Input, null);
                }
                catch (ObjectDisposedException)
                {
                    _logger?.LogWarning(nameof(UdpClient) + "已被释放");
                    Dispose();
                }
                
            }
        }

        private async Task ProcessInputDataAsync(uint conversationId, ReadOnlyMemory<byte> data)
        {
            // 确保 Kcp 不同时为服务器和客户端
            if (IsServer && IsClient)
                throw new ArgumentException("Kcp cannot be both server and client at the same time.");

            // 确保 Kcp 是服务器或客户端之一
            if (!IsServer && !IsClient)
                throw new ArgumentException("Kcp must be either server or client.");

            await Task.Run(async () =>
            {
                try
                {
                    // 检查是否是错误包
                    if (data.Length > Overhead && data.Span[4] is ErrorCommand.ConversationId_Error)
                    {
                        CheckError(data.Span[4]);
                        return;
                    }

                    // 客户端逻辑
                    if (IsClient)
                    {
                        if (conversationId == ConversationId) // 比较会话ID
                        {
                            RemoteEndPoint = ReceiveEndPoint!;
                            ReceiveQueue.Enqueue(ParseSegment(data.Span));
                        }
                        else
                        {
                            // 触发错误事件
                            OnError?.Invoke(this, new ConnectionEventArgs(ReceiveEndPoint, "Error"));
                        
                            // 发送错误包
                            await Error_ConversationIdAsync(BitConverter.ToUInt32(data.Span[8..12]));
                        }
                    }
                    // 服务器逻辑
                    else if (IsServer)
                    {
                        RemoteEndPoint = ReceiveEndPoint!;
                        if (ReceiveEndPoint?.Address.Equals(RemoteEndPoint.Address) ?? false) // 比较IP
                        {
                            ReceiveQueue.Enqueue(ParseSegment(data.Span));
                        }
                        else
                        {
                            // 触发错误事件
                            OnError?.Invoke(this, new ConnectionEventArgs(ReceiveEndPoint, "Error"));
                        
                            // 发送错误包
                            await Error_ConversationIdAsync(BitConverter.ToUInt32(data.Span[8..12]));
                        }
                    }
                }
                catch (ArgumentOutOfRangeException ex)
                {
                    _logger?.LogWarning("ArgumentOutOfRangeException in ParseSegment: " + ex.Message);
                    _logger?.LogWarning("Data length: " + data.Length);
                    _logger?.LogWarning("Data: " + BitConverter.ToString(data.ToArray()));
                    throw;
                }
                catch (Exception ex)
                {
                    _logger?.LogError("Exception in ProcessInputDataAsync: " + ex.Message);
                }
                
            });
            
            
        }

        /// <summary>
        /// 发送数据,存入发送队列
        /// </summary>
        private void SendData(ReadOnlySpan<byte> data) => SendQueue.Enqueue(PackSegmentAuto(data));

        private void SendData(Segment segment) => SendQueue.Enqueue(segment);
        
        private async Task SendDataAsync(ReadOnlyMemory<byte> data) => await Task.Run(() => SendData(data.Span));
        
        private async Task SendDataAsync(Segment segment) => await Task.Run(() => SendData(segment));

        private void SendDataFast(ReadOnlySpan<byte> data) => SendQueue.Enqueue(PackSegmentFastAuto(data));

        private void SendDataFast(Segment segment) => SendQueue.Enqueue(segment);
        
        private async Task SendDataFastAsync(Segment segment) => await Task.Run(() => SendDataFast(segment));
        
        private async Task SendDataFastAsync(ReadOnlyMemory<byte> segment) => await Task.Run(() => SendDataFast(segment.Span));

        /// <summary>
        /// 内部从ReceiveBuffer获取数据
        /// </summary>
        private Segment GetData(uint sequenceNumber)
        {
            try
            {
                if (!ReceiveBuffer.ContainsKey(sequenceNumber)) return null!;

                ReceiveBuffer.Remove(sequenceNumber, out var value);
                return value!;
            }
            catch (InvalidOperationException)
            {
                throw new InvalidOperationException(nameof(GetData));
            }
        }

        private Segment GetFirstData()
        {
            try
            {
                if (ReceiveBuffer.Count is 0) throw new InvalidOperationException(nameof(GetFirstData));

                ReceiveBuffer.Remove(ReceiveBuffer.Keys.First(), out var value);
                return value!;
            }
            catch (InvalidOperationException)
            {
                throw new InvalidOperationException(nameof(GetFirstData));
            }
        }

        /// <summary>
        /// 处理数据的应用层
        /// </summary>
        /// <exception cref="ArgumentException"></exception>
        private async ValueTask ReceiveAsync()
        {
            try
            {
                while (ReceiveQueue.TryDequeue(out var segment))
                {
                    if (ReceiveNumber >= Math.Min(ReceiveWindow, (int)CongestionWindow * MaximumTransferUnit))
                        break; // 接收拥塞控制

                    switch (segment.Command)
                    {
                        case CommandPush: await HandlePushDataAsync(segment); break;
                        case CommandPushFast: await HandlePushDataFastAsync(segment); break;
                        case CommandAck: await HandleAck(segment); break;
                        case CommandProbeInquire: await HandleWindowInquire(segment); break;
                        case CommandProbeInform: HandleWindowInform(ref segment); break;
                        default: throw new ArgumentException("Unknown command");
                    }

                    UpdateRecentTimestamp(); // 更新时间戳
                    Interlocked.Increment(ref ReceiveNumber);
                    
                    //数据接收事件
                    OnDataReceived?.Invoke(this, new DataEventArgs(segment.Data));
                }
            }
            finally
            {
                ReceiveCongestionControl(); // 重新计算接受窗口
                Interlocked.Exchange(ref ReceiveNumber, 0);
            }

        }

        #region 命令处理

        /// <summary>
        /// 处理数据包
        /// </summary>
        /// <param name="segment"></param>
        private async ValueTask HandlePushDataAsync(Segment segment)
        {
            try
            {
                // 如果序列号不在 AckList 中，处理丢包或正常序列
                if (!AckList.Contains(segment.SequenceNumber))
                {
                    // 添加序列号到 AckList
                    for (var i = ReceiveAckCount + 1; i < segment.SequenceNumber; i++)
                    {
                        AckList.Add(i);
                    }

                    // 更新 TransmitCount 和 ReceiveAckCount
                    TransmitCount = segment.SequenceNumber;
                    ReceiveAckCount = segment.SequenceNumber;
                }

                // 添加进接收队列
                ReceiveBuffer.TryAdd(segment.SequenceNumber, segment);



            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
            }
            finally
            {
                // 发送确认包
                await SendAckPacketAsync(segment.SequenceNumber);
            }
        }

        /// <summary>
        /// 处理数据包（快速包）
        /// </summary>
        /// <param name="segment"></param>
        private async ValueTask HandlePushDataFastAsync(Segment segment) => await HandlePushDataAsync(segment);

        /// <summary>
        /// 处理确认包
        /// </summary>
        /// <param name="segment"></param>
        private async ValueTask HandleAck(Segment segment)
        {
            TimerPool.UnregisterTimer(segment.SequenceNumber); //接收到了Ack，注销自动重传
            _logger?.LogInfo("接收到了确认包");
            Debug.LogWarning("接收到了确认包");
            if (PacketLoss.TryRemove(segment.SequenceNumber, out var value)) //检测是否为丢包重传
            {
                SegmentPool.Return(value.Segment!);
                return;
            }

            if (segment.SequenceNumber < ReceiveAckCount) return;
            
            if (segment.SequenceNumber - ReceiveAckCount >= RetransmitThreshold) //通过确认包判断是否丢包(阈值)
            {
                //快速重传(快速包仅重传一次)
                if (!IsDelayAck) //开启了延迟ack不能快速重传
                    Retransmission(segment.SequenceNumber);

            }
            else //正确接收到确认包
            {
                if (!SendBuffer.TryRemove(segment.SequenceNumber, out var seg)) return; //移除SendBuffer
                //TimerPool.UnregisterTimer(seg!.SequenceNumber); //注销自动重传
                AckList.Add(seg.SequenceNumber); //添加进确认表
                
                await EndAutoRetransmissionControl(GetCurrentULongTimestamp(), seg.SequenceNumber); //动态计算Rto
                
                segment.Dispose(); // 释放资源
            
                SegmentPool.Return(seg); //归还Segment
            }

            // 更新发送窗口大小
            SendWindow = segment.WindowSize;


            ReceiveAckCount = segment.SequenceNumber; //确认id++
            
        }

        /// <summary>
        /// 处理窗口询问包
        /// </summary>
        /// <param name="segment"></param>
        private async Task HandleWindowInquire(Segment segment)
        {
            using var buffer = BinarySegmentProbe(PackSegmentAutoProbeInform());
            _logger?.LogInfo("接收到了探测包");
            try
            {
                
                ReadOnlyMemory<byte> dataToSend = buffer.Memory;

                // 加密数据
                if (_encryptor is not null)
                {
                    dataToSend = _encryptor.Encrypt(dataToSend);
                }
                
                await UdpClient.SendAsync(dataToSend.ToArray(),ProbeOverhead, RemoteEndPoint);
            }
            catch (Exception e)
            {
                _logger?.LogError(e.Message);
                throw;
            }
            finally
            {
                segment.Dispose();
            }

        }

        /// <summary>
        /// 处理窗口告知包
        /// </summary>
        /// <param name="segment"></param>
        private void HandleWindowInform(ref Segment segment)
        {
            _logger?.LogInfo("接收到了探测告知包");
            RecentTimestamp = GetCurrentULongTimestamp();
            _isFirstProbe = true;
            _probeStopwatch.Stop();
            Interlocked.Exchange(ref ProbeCount, 0);
            segment.Dispose();
        }

        #endregion

        #region 错误处理

        private void CheckError(byte command)
        {
            switch (command)
            {
                case ErrorCommand.ConversationId_Error: TransmitCount--; break; //减少发送包id，关闭计时器等
            }
        }

        /// <summary>
        /// 发送会话错误包
        /// </summary>
        /// <param name="sequenceNumber"></param>
        private async Task Error_ConversationIdAsync(uint sequenceNumber)
        {
            SegmentError segmentError = new SegmentError
            {
                ConversationId = this.ConversationId,
                Command = ErrorCommand.ConversationId_Error,
                SequenceNumber = sequenceNumber
            };

            var buffer = ArrayPool.Rent(9);

            try
            {
                BitConverter.TryWriteBytes(buffer[..4], segmentError.ConversationId);
                buffer[4] = segmentError.Command;
                BitConverter.TryWriteBytes(buffer[5..9], segmentError.SequenceNumber);
                

                await UdpClient.SendAsync(buffer, buffer.Length, RemoteEndPoint);
                _logger?.LogWarning("发送错误包成功");
                
            }
            finally
            {
                ArrayPool.Return(buffer);
            }


        }

        #endregion

        #region 延迟Ack算法

        /// <summary>
        /// 异步发送确认包
        /// </summary>
        /// <param name="sequenceNumber"></param>
        private async ValueTask SendAckPacketAsync(uint sequenceNumber)
        {
            if (IsDelayAck) await Task.Delay(AckInterval).ConfigureAwait(false);
            using var buffer = BinarySegmentAck(sequenceNumber);

            try
            {
                
                ReadOnlyMemory<byte> dataToSend = buffer.Memory;

                // 加密数据
                if (_encryptor is not null)
                {
                    dataToSend = _encryptor.Encrypt(dataToSend);
                }
                
                await UdpClient.SendAsync(dataToSend.ToArray(), AckOverhead, RemoteEndPoint);
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                throw;
            }



        }

        #endregion

        #region 重传算法

        /// <summary>
        /// 快速重传
        /// </summary>
        private void Retransmission(uint sequenceNumber)
        {
            _logger?.LogWarning("快速重传");
            Debug.LogError($"快速重传{sequenceNumber}");
            for (var i = ReceiveAckCount + 1; i < sequenceNumber; i++)
            {
                if (AckList.Contains(i))
                {
                    TimerPool.UnregisterTimer(i);
                    continue;
                }
                
                if(!SendBuffer.TryGetValue(i, out var buffer)) continue;
                
                switch (buffer.Command)
                {
                    case CommandPush: // 非快速包需进入重传集合
                        if (SendBuffer.TryRemove(i, out var segmentData))
                        {
                            //TimerPool.UnregisterTimer(segmentData.SequenceNumber); // 注销自动重传
                            PacketLoss.TryAdd(i, new PacketLoss(segmentData, GetCurrentULongTimestamp(),
                                LossType.普通包, 1)); // 添加进丢包列表
                            SendData(PacketLoss[i].Segment!); // 第一次快速重传，之后的重传由集合控制哦
                        }

                        break;
                    case CommandPushFast: // 快速包，仅重传一次哦
                        if (SendBuffer.TryRemove(i, out var segmentDataFast))
                        {
                            //TimerPool.UnregisterTimer(segmentDataFast.SequenceNumber);
                            PacketLoss.TryAdd(i, new PacketLoss(segmentDataFast, GetCurrentULongTimestamp(),
                                LossType.快速包, 1)); // 添加进丢包列表
                            SendDataFast(segmentDataFast);
                        }

                        break;
                }

                AckList.Add(i);
            }
        }

        /// <summary>
        /// 自动重传
        /// </summary>
        /// <param name="sequenceNumber"></param>
        private void AutoRetransmission(uint sequenceNumber)
        {
            if (!SendBuffer.TryGetValue(sequenceNumber, out var segment)) return;
            
            _logger?.LogWarning("自动重传");
            Debug.LogError($"自动重传{sequenceNumber}");
            
            // 重传逻辑
            SendData(segment);
            _logger?.LogWarning($"重传包，序列号：{sequenceNumber}");
        }

        /// <summary>
        /// 超过最大重传次数，移除重传包
        /// </summary>
        private async ValueTask RemoveRetransmission()
        {
            if (RemovePacketLoss.Count is 0) return;

            await Task.Run(() =>
            {
                foreach (var packet in RemovePacketLoss)
                {
                    PacketLoss.TryRemove(packet.Value.Segment!.SequenceNumber, out var _);
                    
                    SegmentPool.Return(packet.Value.Segment); //归还Segment
                }
            });


        }

        #endregion

        #region 拥塞控制算法

        /// <summary>
        /// 拥塞窗口计算
        /// </summary>
        private async Task CongestionControl()
        {

            await Task.Run(() =>
            {
                // 检查当前未确认包数量
                int currentLossCount = PacketLoss.Count;

                // 拥塞事件处理逻辑
                if (currentLossCount > 0)
                {
                    // 首次进入拥塞状态
                    if (!CongestionRecoveryPhase)
                    {
                        CongestionLastLossCount = currentLossCount;
                        CongestionThreshold = (uint)CongestionWindow / 2;
                        CongestionWindow = (ushort)Math.Max(CongestionThreshold, 1);
                        CongestionAddition = 0;
                        CongestionRecoveryPhase = true;
                        _logger?.LogDebug($"首次拥塞检测 | 窗口: {CongestionWindow} | 丢包阈值: {CongestionLastLossCount}");
                    }
                    // 已处于恢复期且丢包数超过上次记录
                    else if (currentLossCount > CongestionLastLossCount)
                    {
                        // 连续拥塞时多次减半
                        CongestionThreshold = (uint)CongestionWindow / 2;
                        CongestionWindow = (ushort)Math.Max(CongestionThreshold, 1);
                        CongestionLastLossCount = currentLossCount; // 更新参考值
                        _logger?.LogDebug($"持续拥塞 | 窗口: {CongestionWindow} | 新阈值: {CongestionLastLossCount}");
                    }
                }
                else
                {
                    // 退出恢复期时重置状态
                    if (CongestionRecoveryPhase)
                    {
                        CongestionLastLossCount = 0;
                        CongestionRecoveryPhase = false;
                        _logger?.LogDebug("退出恢复期，重置丢包计数器");
                    }

                    // 正常窗口增长策略
                    if (CongestionWindow < CongestionThreshold)
                    {
                        // 慢启动
                        CongestionWindow += 1;
                    }
                    else
                    {
                        // 拥塞避免：精确线性增长
                        CongestionAddition += 1f / CongestionWindow;
                        if (CongestionAddition >= 1)
                        {
                            CongestionWindow += 1;
                            CongestionAddition -= 1;
                        }
                    }

                    // 窗口上限保护
                    CongestionWindow = Math.Min(CongestionWindow, MaxWindowSize);
                }
            });

        }

        /// <summary>
        /// 动态计算接收窗口
        /// </summary>
        private void ReceiveCongestionControl()
        {
            ReceiveNumber = Convert.ToInt32(MaximumTransferUnit * ReceiveBuffer.Count);
        }
        

        #endregion

        #region 动态RTO计算
        
        /// <summary>
        /// 动态RTO计算(Begin)
        /// </summary>
        /// <param name="sequenceNumber"></param>
        private void BeginAutoRetransmissionControl(uint sequenceNumber)
        {
            if (sequenceNumber % RttSamplingInterval is not 0) return; //抽样
            
            RoundTripTimeSequenceNumber = sequenceNumber;

            RoundTripRecordTime = GetCurrentULongTimestamp();
        }
        
        /// <summary>
        /// 动态RTO计算(End)
        /// </summary>
        /// <param name="currentTimestamp"></param>
        /// <param name="sequenceNumber"></param>
        private async ValueTask EndAutoRetransmissionControl(ulong currentTimestamp, uint sequenceNumber)
        {
            if(RoundTripTimeSequenceNumber is 0) return;
            
            if(RoundTripTimeSequenceNumber != sequenceNumber) return;

            RoundTripTime = currentTimestamp - RoundTripRecordTime;
            
            await UpdateRto(RoundTripTime);
        }

        /// <summary>
        /// 刷新Rto
        /// SRTT = (1 - α) * SRTT + α * RTT_sample
        /// RTTVAR = (1 - β) * RTTVAR + β * |delta|
        /// </summary>
        /// <param name="roundTripTime"></param>
        private async Task UpdateRto(ulong roundTripTime)
        {
            await Task.Run(() =>
            {
                if (SmoothRoundTripTime == 0)
                {
                    SmoothRoundTripTime = roundTripTime;
                    RoundTripTimeVariance = roundTripTime / 2;
                }
                else
                {
                    long delta = (long)roundTripTime - (long)SmoothRoundTripTime;
                    ulong absDelta = (ulong)Math.Abs(delta);

                    // 使用RFC6298标准公式计算平滑RTT和方差
                    SmoothRoundTripTime = SmoothRoundTripTime - (SmoothRoundTripTime >> 3) + (roundTripTime >> 3); // α=1/8
                    RoundTripTimeVariance = RoundTripTimeVariance - (RoundTripTimeVariance >> 2) + (absDelta >> 2); // β=1/4
                }

                // 计算RTO并应用边界限制
                ulong calculatedRto = SmoothRoundTripTime + Math.Max(4 * RoundTripTimeVariance, MinimumRto);
                ReceiveRoundTripTimeout = Math.Clamp(calculatedRto, MinimumRto, MaximumRto);

                _logger?.LogDebug(
                    $"RTO Updated| SRTT:{SmoothRoundTripTime}ms RTTVAR:{RoundTripTimeVariance}ms " +
                    $"RTO:{ReceiveRoundTripTimeout}ms");
                Debug.LogError(
                    $"RTO Updated| SRTT:{SmoothRoundTripTime}ms RTTVAR:{RoundTripTimeVariance}ms " +
                    $"RTO:{ReceiveRoundTripTimeout}ms");
            });
            
        }

        #endregion

        #region 打包

        private Segment PackSegment(ReadOnlySpan<byte> data) => new Segment
        {
            ConversationId = BinaryPrimitives.ReadUInt32LittleEndian(data[.. 4]),
            Command = data[4],
            Fragment = data[5],
            WindowSize = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(6, 2)),
            SequenceNumber = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(8, 4)),
            Length = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(12, 2)),
            Data = data.ToArray().AsMemory(Overhead, data.Length)
        };

        private Segment PackSegmentAuto(ReadOnlySpan<byte> data)
        {
            var segment = SegmentPool.Rent(); //从池里租用
            
            segment.ConversationId = this.ConversationId;
            segment.Command = CommandPush;
            segment.Fragment = 0;
            segment.WindowSize = this.CongestionWindow;
            segment.SequenceNumber = ++this.TransmitCount;
            segment.Length = (ushort)data.Length;
            segment.Data = data.ToArray();
            
            return segment;
        }

        private Segment PackSegmentFastAuto(ReadOnlySpan<byte> data)
        {
            var segment = SegmentPool.Rent(); //从池里租用
            
            segment.ConversationId = this.ConversationId;
            segment.Command = CommandPushFast;
            segment.Fragment = 0;
            segment.WindowSize = this.CongestionWindow;
            segment.SequenceNumber = ++this.TransmitCount;
            segment.Length = (ushort)data.Length;
            segment.Data = data.ToArray();
            
            return segment;
        }

        private SegmentAck PackSegmentAutoAck(uint sequenceNumber)
        {
            var segment = SegmentAckPool.Rent(); //从池里租用
            segment.ConversationId = this.ConversationId;
            segment.WindowSize = ReceiveWindow;
            segment.SequenceNumber = sequenceNumber;
            
            return segment;
        }

        private SegmentProbeInquire PackSegmentAutoProbeInquire() => new SegmentProbeInquire()
        {
            ConversationId = this.ConversationId,
            Command = CommandProbeInquire
        };

        private SegmentProbeInform PackSegmentAutoProbeInform() => new SegmentProbeInform()
        {
            ConversationId = this.ConversationId,
            Command = CommandProbeInform
        };

        #endregion

        #region 解包

        /// <summary>
        /// 解包数据
        /// </summary>
        /// <param name="data"></param>
        /// <returns></returns>
        private static Segment ParseSegment(ReadOnlySpan<byte> data)
        {
            switch (data.Length)
            {
                case AckOverhead: return ParseSegmentAck(data);
                case ProbeOverhead: return ParseSegmentProbe(data);
            }

            if(data.Length <= Overhead)
                throw new ArgumentException("数据长度不能小于OverHead", nameof(data));
            
            Segment segment = new Segment()
            {
                // 会话编号 (4 字节)
                ConversationId = BinaryPrimitives.ReadUInt32LittleEndian(data[.. 4]),

                // 命令类型 (1 字节)
                Command = data[4],

                // 分片编号 (1 字节)
                Fragment = data[5],

                // 窗口大小 (2 字节)
                WindowSize = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(6, 2)),

                // 序列号 (4 字节)
                SequenceNumber = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(8, 4)),

                // 数据长度 (2 字节)
                Length = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(12, 2)),

                // 数据缓冲区
                // 数据从索引 14 开始，长度为 segment.Length
                Data = data.Slice(Overhead, BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(12, 2))).ToArray()
            };



            return segment;
        }

        private static Segment ParseSegmentAck(ReadOnlySpan<byte> data) => new Segment()
        {
            // 会话编号 (4 字节)
            ConversationId = BinaryPrimitives.ReadUInt32LittleEndian(data[.. 4]),

            // 命令类型 (1 字节)
            Command = data[4],

            // 序列号 (4 字节)
            SequenceNumber = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(5, 4)),

            // 窗口信息 (2 字节)
            WindowSize = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(9, 2)),

        };

        private static Segment ParseSegmentProbe(ReadOnlySpan<byte> data) => new Segment()
        {
            // 会话编号 (4 字节)
            ConversationId = BinaryPrimitives.ReadUInt32LittleEndian(data[.. 4]),

            // 命令类型 (1 字节)
            Command = data[4],


        };

        #endregion

        #region 字节序列化

        private IMemoryOwner<byte> BinarySegment(Segment segment)
        {
            // 从 MemoryPool 中租用内存
            var memoryOwner = MemoryPool.Rent(segment.Data.Length + Overhead);
            try
            {
                // 创建一个 Span<byte> 来操作内存
                Span<byte> span = memoryOwner.Memory.Span[.. (segment.Data.Length + Overhead)];

                // 写入头部数据
                int offset = 0;
                BitConverter.TryWriteBytes(span.Slice(offset, 4), segment.ConversationId);
                offset += 4;
                span[offset++] = segment.Command;
                span[offset++] = segment.Fragment;
                BitConverter.TryWriteBytes(span.Slice(offset, 2), segment.WindowSize);
                offset += 2;
                BitConverter.TryWriteBytes(span.Slice(offset, 4), segment.SequenceNumber);
                offset += 4;
                BitConverter.TryWriteBytes(span.Slice(offset, 2), segment.Length);
                offset += 2;

                // 将 Data 字段的内容复制到 span
                segment.Data.Span.CopyTo(span[offset..]);

                // 返回 ReadOnlyMemory<byte>
                return memoryOwner;
            }
            catch
            {
                // 如果发生异常，归还内存
                memoryOwner.Dispose();
                throw;
            }
        }

        private IMemoryOwner<byte> BinarySegmentAck(uint sequenceNumber)
        {
            var memoryOwner = MemoryPool.Rent(AckOverhead);
            var ack = PackSegmentAutoAck(sequenceNumber);
            try
            {
                
                Span<byte> span = memoryOwner.Memory.Span[.. AckOverhead];

                int offset = 0;
                BitConverter.TryWriteBytes(span.Slice(offset, 4), ack.ConversationId);
                offset += 4;
                span[offset++] = 82;
                BitConverter.TryWriteBytes(span.Slice(offset, 4), ack.SequenceNumber);
                offset += 4;
                BitConverter.TryWriteBytes(span.Slice(offset, 2), ack.WindowSize);

                return memoryOwner; // 返回 IMemoryOwner<byte>
            }
            catch
            {
                memoryOwner.Dispose(); // 如果发生异常，释放内存
                throw;
            }
            finally
            {
                SegmentAckPool.Return(ack); //归还SegmentAck
            }
        }

        private IMemoryOwner<byte> BinarySegmentProbe(SegmentProbeInquire segment)
        {
            var memoryOwner = MemoryPool.Rent(ProbeOverhead);
            try
            {
                Span<byte> span = memoryOwner.Memory.Span[..ProbeOverhead];
                
                BitConverter.TryWriteBytes(span[..4], segment.ConversationId);
                span[4] = segment.Command;

                return memoryOwner; // 返回 IMemoryOwner<byte>
            }
            catch
            {
                memoryOwner.Dispose(); // 如果发生异常，释放内存
                throw;
            }
        }
        
        private IMemoryOwner<byte> BinarySegmentProbe(SegmentProbeInform segment)
        {
            var memoryOwner = MemoryPool.Rent(ProbeOverhead);
            try
            {
                Span<byte> span = memoryOwner.Memory.Span[..ProbeOverhead];
                
                BitConverter.TryWriteBytes(span[..4], segment.ConversationId);
                span[4] = segment.Command;

                return memoryOwner; // 返回 IMemoryOwner<byte>
            }
            catch
            {
                memoryOwner.Dispose(); // 如果发生异常，释放内存
                throw;
            }
        }

        #endregion

        /// <summary>
        /// 获取当前时间戳
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ulong GetCurrentULongTimestamp() =>
            (ulong)(DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalMilliseconds;

        private static uint GetCurrentUIntTimestamp() =>
            (uint)(DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalMilliseconds;

        private static T GetCurrentTimestamp<T>() where T : IConvertible =>
            (T)Convert.ChangeType((DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalMilliseconds, typeof(T));

        /// <summary>
        /// 更新时间戳
        /// </summary>
        private void UpdateRecentTimestamp()
        {
            RecentTimestamp = GetCurrentULongTimestamp();
        }

        #region 外部调用

        public void Test()
        {
            Console.WriteLine(ConversationId);
            Console.WriteLine(TransmitCount);
            Console.WriteLine(ReceiveAckCount);
            Console.WriteLine(TimerPool.GetPoolCount());
            Console.WriteLine(SegmentPool.GetPoolCount());
            Console.WriteLine(SegmentAckPool.GetPoolCount());
        }

        /// <summary>
        /// 外部调用获取数据（第一个数据）
        /// </summary>
        public ReadOnlyMemory<byte> GetData()
        {
            _lock.EnterReadLock();
            try
            {
                if (ReceiveBuffer.Count is 0) return ReadOnlyMemory<byte>.Empty; // 确保字典不为空

                return GetFirstData()?.Data ?? ReadOnlyMemory<byte>.Empty;
            }
            catch (InvalidOperationException)
            {
                return ReadOnlyMemory<byte>.Empty;
            }
            finally
            {
                _lock.ExitReadLock();
            }
        }
        
        public async Task<ReadOnlyMemory<byte>> GetDataAsync() => await Task.Run(GetData);

        public int GetDataCount() => ReceiveBuffer.Count;

        /// <summary>
        /// 外部调用发送数据
        /// </summary>
        /// <param name="data"></param>
        /// <param name="endPoint"></param>
        public void Send(ReadOnlySpan<byte> data, IPEndPoint endPoint) => SendData(data);

        public void Send(ReadOnlySpan<byte> data) => SendData(data);
        
        public async Task SendAsync(ReadOnlyMemory<byte> data, IPEndPoint endPoint) => await SendDataAsync(data);
        
        public async Task SendAsync(ReadOnlyMemory<byte> data) => await SendDataAsync(data);

        public void SendFast(ReadOnlySpan<byte> data, IPEndPoint endPoint) => SendData(data);

        public void SendFast(ReadOnlySpan<byte> data) => SendDataFast(data);

        public async Task SendFastAsync(ReadOnlyMemory<byte> data, IPEndPoint endPoint) => await SendDataFastAsync(data);
        
        public async Task SendFastAsync(ReadOnlyMemory<byte> data) => await SendDataFastAsync(data);


        /// <summary>
        /// 外部调用接收数据(Legacy)
        /// </summary>
        /// <param name="data"></param>
        public void Input(ReadOnlySpan<byte> data) => ReceiveQueue.Enqueue(ParseSegment(data));
        
        /// <summary>
        /// 外部调用关闭连接
        /// </summary>
        public void Close() => Dispose();

        /// <summary>
        /// 外部调用设置拥塞窗口大小
        /// </summary>
        /// <param name="windowSize"></param>
        public void SetWindowSize(ushort windowSize)
        {
            this.CongestionWindow = windowSize;
        }
        
        /// <summary>
        /// 外部调用获取流量监控数据
        /// </summary>
        /// <returns></returns>
        /// <exception cref="ArgumentException"></exception>
        public KcpTrafficMonitorSetting GetTrafficMonitor()
        {
            if (_trafficMonitorSetting is null)
                throw new ArgumentException("没有设置流量监控");
            
            return _trafficMonitorSetting;
        }

        /// <summary>
        /// 外部调用关闭日志
        /// </summary>
        public void CloseLogger()
        {
            _logger = null;
        }

        /// <summary>
        /// 外部调用设置延迟Ack
        /// </summary>
        /// <param name="isDelay"></param>
        /// <param name="interval"></param>
        public void SetAckDelay(bool isDelay, int interval) =>
            ((IExternalSetting)this).SetAckDelay(isDelay, interval);

        /// <summary>
        /// 外部调用设置Update更新频率
        /// </summary>
        /// <param name="interval"></param>
        public void SetUpdateFrequency(double interval) =>
            ((IExternalSetting)this).SetUpdateFrequency(interval);

        /// <summary>
        /// 外部调用设置Kcp行为模式
        /// </summary>
        /// <param name="kcpBehaviourSetting"></param>
        public void SetKcpBehaviour(KcpBehaviourSetting kcpBehaviourSetting) => 
            ((IExternalSetting)this).SetKcpBehaviour(kcpBehaviourSetting);
        
        /// <summary>
        /// 外部调用设置Kcp死亡link和probe次数
        /// </summary>
        /// <param name="kcpDeadLinkSetting"></param>
        public void SetKcpDeadLink(KcpDeadLinkSetting kcpDeadLinkSetting) => 
            ((IExternalSetting)this).SetKcpDeadLink(kcpDeadLinkSetting);
        
        /// <summary>
        /// 外部调用设置Kcp流量监控
        /// </summary>
        /// <param name="kcpTrafficMonitorSetting"></param>
        public void SetKcpTrafficMonitor(KcpTrafficMonitorSetting kcpTrafficMonitorSetting) => 
            ((IExternalSetting)this).SetKcpTrafficMonitor(kcpTrafficMonitorSetting);
        
        /// <summary>
        /// 外部调用设置Kcp加密算法
        /// </summary>
        /// <param name="kcpEncryptor"></param>
        public void SetKcpEncrypt(IKcpEncryptor kcpEncryptor) => 
            ((IExternalSetting)this).SetKcpEncrypt(kcpEncryptor);
        
        /// <summary>
        /// 外部调用设置Kcp快速重传阈值
        /// </summary>
        /// <param name="kcpReTransmitThresholdSetting"></param>
        public void SetKcpReTransmitThreshold(KcpRetransmissionThresholdSetting kcpReTransmitThresholdSetting) => 
            ((IExternalSetting)this).SetKcpReTransmitThreshold(kcpReTransmitThresholdSetting);
        
        /// <summary>
        /// 外部调用设置Kcp超时重传时间
        /// </summary>
        /// <param name="kcpAutoRetransmitSetting"></param>
        public void SetKcpAutoRetransmit(KcpAutoRetransmitSetting kcpAutoRetransmitSetting) => 
            ((IExternalSetting)this).SetKcpAutoRetransmit(kcpAutoRetransmitSetting);
        
        /// <summary>
        /// 外部调用设置Kcp日志接口
        /// </summary>
        /// <param name="kcpLogger"></param>
        public void SetKcpLogger(IKcpLogger kcpLogger) => 
            ((IExternalSetting)this).SetKcpLogger(kcpLogger);
        
        void IExternalSetting.SetAckDelay(bool isDelay, int interval)
        {
            IsDelayAck = isDelay;
            
            AckInterval = interval;
        }

        void IExternalSetting.SetUpdateFrequency(double interval)
        {
            _timer.Interval = interval;
        }

        void IExternalSetting.SetKcpBehaviour(KcpBehaviourSetting kcpBehaviourSetting)
        {
            this.IsServer = kcpBehaviourSetting.IsServer;
            
            this.IsClient = kcpBehaviourSetting.IsClient;
            
        }

        void IExternalSetting.SetKcpDeadLink(KcpDeadLinkSetting kcpDeadLinkSetting)
        {
            this.DeadLinkCount = kcpDeadLinkSetting.DeadLinkCount;
            this.DeadProbeCount = kcpDeadLinkSetting.DeadProbeCount;
        }

        void IExternalSetting.SetKcpTrafficMonitor(KcpTrafficMonitorSetting kcpTrafficMonitorSetting)
        {
            this._trafficMonitorSetting = kcpTrafficMonitorSetting;
        }

        void IExternalSetting.SetKcpEncrypt(IKcpEncryptor kcpKcpEncryptor)
        {
            this._encryptor = kcpKcpEncryptor;
        }

        void IExternalSetting.SetKcpReTransmitThreshold(KcpRetransmissionThresholdSetting kcpRetransmissionThreshold)
        {
            this.RetransmitThreshold = kcpRetransmissionThreshold.RetransmissionThreshold;
        }

        void IExternalSetting.SetKcpAutoRetransmit(KcpAutoRetransmitSetting kcpAutoRetransmitSetting)
        {
            ReceiveRoundTripTimeout = kcpAutoRetransmitSetting.RetransmitInterval;

            if (kcpAutoRetransmitSetting.RttSamplingInterval is not 0)
                RttSamplingInterval = kcpAutoRetransmitSetting.RttSamplingInterval;
        }

        void IExternalSetting.SetKcpLogger(IKcpLogger kcpLogger)
        {
            this._logger = kcpLogger;
        }

        #endregion

        #region 事件注册，注销

        public void AddConnectEventListener(ConnectionEventHandler handler)
        {
            OnConnected += handler;
        }

        public void RemoveConnectEventListener(ConnectionEventHandler handler)
        {
            OnConnected -= handler;
        }

        public void AddDisconnectEventListener(ConnectionEventHandler handler)
        {
            OnDisconnected += handler;
        }

        public void RemoveDisconnectEventListener(ConnectionEventHandler handler)
        {
            OnDisconnected -= handler;
        }

        public void AddErrorEventListener(ConnectionEventHandler handler)
        {
            OnError += handler;
        }

        public void RemoveErrorEventListener(ConnectionEventHandler handler)
        {
            OnError -= handler;
        }

        public void AddDataReceivedEventListener(DataEventHandler handler)
        {
            OnDataReceived += handler;
        }

        public void RemoveDataReceivedEventListener(DataEventHandler handler)
        {
            OnDataReceived -= handler;
        }

        public void AddDataSentEventListener(DataEventHandler handler)
        {
            OnDataSent += handler;
        }

        public void RemoveDataSentEventListener(DataEventHandler handler)
        {
            OnDataSent -= handler;
        }

        #endregion

        #region 工具

        /// <summary>
        /// 初始化Kcp行为模式
        /// </summary>
        public void SetMode(bool isServer, bool isClient)
        {
            if (isServer && isClient)
                throw new ArgumentException("Kcp cannot be both server and client at the same time.");

            if (!isServer && !isClient)
                throw new ArgumentException("Kcp must be either server or client.");

            IsServer = isServer;
            IsClient = isClient;
            
        }

        #endregion


        public void Dispose()
        {
            // 触发连接断开事件
            OnDisconnected?.Invoke(this, new ConnectionEventArgs(RemoteEndPoint, "Connection disconnected"));
            
            //重置流量监控
            _trafficMonitorSetting?.ResetStatistics();
            
            _timer.Close();
            _timer.Dispose();
            _probeStopwatch.Stop();
            TimerPool.Dispose();
            SegmentPool.Dispose();
            SegmentAckPool.Dispose();
            UdpClient.Dispose();
            
        }
    }
    
    
    
}
