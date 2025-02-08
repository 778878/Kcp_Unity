using System;

namespace Kcp.Timer
{
    public enum TimerStation
    {
        NotWorked,
        DoWorking,
        DoneWorked
    }
    
    #nullable enable
    public sealed class PacketTimer : IDisposable
    {
    
        private float StartTime { get; set; }
        
        private Action? Action { get; set; }
        
        private bool IsStopTime { get; set; }
        
        public TimerStation Station { get; private set; }
        
        public PacketTimer()
        {
            InitTimer();
        }
    
        public void StartTimer(float startTime, Action action)
        {
            StartTime = startTime;
            Action = action;
            Station = TimerStation.DoWorking;
            IsStopTime = false;
        }
    
        public void Update(long deltaTime)
        {
            if(IsStopTime) return;
            
            StartTime -= deltaTime;
            if (StartTime <= 0)
            {
                Action?.Invoke();
                StopTimer();
            }
        }
        
        public void InitTimer()
        {
            StartTime = 0;
            Action = null;
            Station = TimerStation.NotWorked;
        }
    
        public void StopTimer()
        {
            IsStopTime = true;
            Station = TimerStation.DoneWorked;
        }
        
        public float GetCurrentTime() => StartTime;
        public void Dispose()
        {
            StartTime = 0;
            Action = null;
        }
    }
}

