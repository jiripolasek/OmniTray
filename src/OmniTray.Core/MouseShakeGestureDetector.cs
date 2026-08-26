// ------------------------------------------------------------
//
// Copyright (c) Jiří Polášek. All rights reserved.
//
// ------------------------------------------------------------

namespace OmniTray;

public sealed class MouseShakeGestureDetector
{
    private readonly AxisState _horizontal = new();
    private readonly TimeSpan _maximumDuration;
    private readonly int _minimumStrokeDistance;
    private readonly int _requiredReversals;
    private readonly AxisState _vertical = new();
    private bool _hasAnchor;
    private bool _isTriggered;
    private DateTimeOffset? _lastSampleAt;

    public MouseShakeGestureDetector(
        int minimumStrokeDistance = 32,
        int requiredReversals = 3,
        TimeSpan? maximumDuration = null)
    {
        if (minimumStrokeDistance <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(minimumStrokeDistance));
        }

        if (requiredReversals <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(requiredReversals));
        }

        this._maximumDuration = maximumDuration ?? TimeSpan.FromMilliseconds(700);
        if (this._maximumDuration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumDuration));
        }

        this._minimumStrokeDistance = minimumStrokeDistance;
        this._requiredReversals = requiredReversals;
    }

    public bool Update(int x, int y, DateTimeOffset timestamp)
    {
        if (!this._hasAnchor ||
            this._lastSampleAt is { } lastSampleAt &&
            (timestamp < lastSampleAt || timestamp - lastSampleAt > this._maximumDuration))
        {
            this.ResetCore(x, y, timestamp);
            return false;
        }

        this._lastSampleAt = timestamp;
        if (this._isTriggered)
        {
            return false;
        }

        if (!this._horizontal.Update(
                x,
                timestamp,
                this._minimumStrokeDistance,
                this._requiredReversals,
                this._maximumDuration) &&
            !this._vertical.Update(
                y,
                timestamp,
                this._minimumStrokeDistance,
                this._requiredReversals,
                this._maximumDuration))
        {
            return false;
        }

        this._isTriggered = true;
        return true;
    }

    public void Reset()
    {
        this._horizontal.Reset();
        this._vertical.Reset();
        this._hasAnchor = false;
        this._isTriggered = false;
        this._lastSampleAt = null;
    }

    private void ResetCore(int x, int y, DateTimeOffset timestamp)
    {
        this._horizontal.Reset(x);
        this._vertical.Reset(y);
        this._hasAnchor = true;
        this._isTriggered = false;
        this._lastSampleAt = timestamp;
    }

    private sealed class AxisState
    {
        private int _anchor;
        private int _direction;
        private int _extreme;
        private int _reversals;
        private DateTimeOffset? _startedAt;

        public bool Update(
            int value,
            DateTimeOffset timestamp,
            int minimumStrokeDistance,
            int requiredReversals,
            TimeSpan maximumDuration)
        {
            if (this._startedAt is { } startedAt && timestamp - startedAt > maximumDuration)
            {
                this.Reset(value);
                return false;
            }

            if (this._direction == 0)
            {
                var distance = value - this._anchor;
                if (Math.Abs(distance) < minimumStrokeDistance)
                {
                    return false;
                }

                this._direction = Math.Sign(distance);
                this._extreme = value;
                this._startedAt = timestamp;
                return false;
            }

            if (this._direction > 0)
            {
                if (value > this._extreme)
                {
                    this._extreme = value;
                    return false;
                }

                if (this._extreme - value < minimumStrokeDistance)
                {
                    return false;
                }

                this._direction = -1;
            }
            else
            {
                if (value < this._extreme)
                {
                    this._extreme = value;
                    return false;
                }

                if (value - this._extreme < minimumStrokeDistance)
                {
                    return false;
                }

                this._direction = 1;
            }

            this._extreme = value;
            this._reversals++;
            return this._reversals >= requiredReversals;
        }

        public void Reset()
        {
            this._anchor = 0;
            this._direction = 0;
            this._extreme = 0;
            this._reversals = 0;
            this._startedAt = null;
        }

        public void Reset(int anchor)
        {
            this.Reset();
            this._anchor = anchor;
            this._extreme = anchor;
        }
    }
}
