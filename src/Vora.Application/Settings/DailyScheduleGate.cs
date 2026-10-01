namespace Vora.Application.Settings;

public sealed class DailyScheduleGate
{
    private TimeSpan? _armedFor;
    private DateTime _lastRunDate = DateTime.MinValue;

    public bool IsDue(TimeSpan scheduledTime, DateTime now)
    {
        if (_armedFor != scheduledTime)
        {
            _armedFor = scheduledTime;
            _lastRunDate = now.TimeOfDay >= scheduledTime ? now.Date : DateTime.MinValue;
        }

        return now.TimeOfDay >= scheduledTime && _lastRunDate < now.Date;
    }

    public void MarkRan(DateTime now) => _lastRunDate = now.Date;
}
