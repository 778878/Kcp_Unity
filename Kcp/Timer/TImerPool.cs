using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace Kcp.Timer
{
    public sealed class TimerPool : IDisposable
{
    private readonly ConcurrentQueue<PacketTimer> _notworkTimers = new ConcurrentQueue<PacketTimer>();
    private readonly ConcurrentDictionary<uint, PacketTimer> _timerDictionary = new ConcurrentDictionary<uint, PacketTimer>();
    private readonly ConcurrentBag<KeyValuePair<uint, PacketTimer>> _removeTimers = new ConcurrentBag<KeyValuePair<uint, PacketTimer>>();
    private float Interval { get; set; } = 10; //默认10ms刷新一次
    private bool IsRunning { get; set; }
    private readonly object _lock = new object();

    public TimerPool(uint initialSize)
    {
        for (uint i = 0; i < initialSize; i++)
        {
            _notworkTimers.Enqueue(CreateNewTimer());
        }

        var updateThread = new Thread(Update) { IsBackground = true };
        IsRunning = true;
        updateThread.Start();
    }

    public TimerPool(uint initialSize, uint interval)
    {
        Interval = interval;
        for (uint i = 0; i < initialSize; i++)
        {
            _notworkTimers.Enqueue(CreateNewTimer());
        }
        
        var updateThread = new Thread(Update) { IsBackground = true };
        IsRunning = true;
        updateThread.Start();
    }

    /// <summary>
    /// 刷新
    /// </summary>
    private void Update()
    {
        var stopwatch = new Stopwatch();
        stopwatch.Start();

        while (IsRunning)
        {
            
            if (!_removeTimers.IsEmpty)
            {
                ResetTimers();
            }

            var deltaTime = stopwatch.ElapsedMilliseconds;
            stopwatch.Restart();

            
            foreach (var timer in _timerDictionary)
            {
                if (timer.Value.Station == TimerStation.DoneWorked)
                {
                    _removeTimers.Add(timer);
                    continue;
                }
                timer.Value.Update(deltaTime);
            }

            Thread.Sleep(TimeSpan.FromMilliseconds(Interval));
        }
    }
    private static PacketTimer CreateNewTimer() => new PacketTimer();

    private void CreateTimer()
    {
        lock (_lock)
        {
            var timer = new PacketTimer();
            _notworkTimers.Enqueue(timer);
        }
    } 
    
    /// <summary>
    /// 外部注册计时器
    /// </summary>
    /// <param name="timerId"></param>
    /// <param name="interval"></param>
    /// <param name="action"></param>
    public void GetOneTimer(uint timerId, float interval, Action action)
    {
        lock (_lock)
        {
            if(_notworkTimers.IsEmpty) CreateTimer(); //如果是空的则创建一个

            if (_notworkTimers.TryDequeue(out var timer))
            {
                timer.StartTimer(interval, action);
                _timerDictionary.TryAdd(timerId, timer);
            }
            else
            {
                throw new InvalidOperationException("Failed to get a timer from the pool.");
            }
            
        }
    }

    public void GetOneTimer(uint timerId, float interval, Action action, out PacketTimer timer)
    {
        lock (_lock)
        {
            if(_notworkTimers.IsEmpty) CreateTimer(); //如果是空的则创建一个

            if (_notworkTimers.TryDequeue(out timer!))
            {
                timer.StartTimer(interval, action);
                _timerDictionary.TryAdd(timerId, timer);
            }
            else
            {
                throw new InvalidOperationException("Failed to get a timer from the pool.");
            }
            
        }
        
    }
    
    /// <summary>
    /// 向外部提供一个销毁计时器的方法
    /// </summary>
    /// <param name="timerName"></param>
    public void UnregisterTimer(uint timerName)
    {
        lock (_lock)
        {
            if (!_timerDictionary.TryGetValue(timerName, out var value)) { return; }
        
            //非工作计时器不能被销毁，因为可能会注册其他事件
            if (value.Station is not TimerStation.DoWorking) { return; }

            value.StopTimer();
        }
        
    }

    private void ResetTimers()
    {
        lock (_lock)
        {
            while (_removeTimers.TryTake(out var timerPair))
            {
                timerPair.Value.InitTimer();
                _timerDictionary.TryRemove(timerPair.Key, out _);
                _notworkTimers.Enqueue(timerPair.Value);
            }
        }
    }

    public void StopUpdatingTimers()
    {
        IsRunning = false;
    }

    public void RestartTimers()
    {
        IsRunning = true;
    }

    public int GetPoolCount()
    {
        lock (_lock)
        {
            return _notworkTimers.Count;
        }
    }

    public void Dispose()
    {
        IsRunning = false;
        lock (_lock)
        {
            foreach (var timer in _timerDictionary.Values)
            {
                timer.Dispose();
            }
            _timerDictionary.Clear();
            _notworkTimers.Clear();
        }
    }
}
}

