using System;

namespace MarqueeDelete
{
    internal sealed class UndoBatchPolicy
    {
        private int m_StableBatches;
        private bool m_SlowFrame;
        private int m_HealthyBatches;
        public int Scale { get; private set; } = 1;
        public int SimpleLimit => Scale * 4;
        public int BuildingLimit => Scale;
        public int NetworkLimit => Scale;
        public int FrameGap { get; private set; } = 1;

        public void ObserveFrame(double seconds)
        {
            if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds <= 0) return;
            if (seconds > 0.05)
            {
                if (m_SlowFrame) return;
                m_SlowFrame = true;
                Scale = Math.Max(1, Scale / 2);
                FrameGap = 2;
                m_StableBatches = 0;
                m_HealthyBatches = 0;
            }
        }

        public void FinishBatch(double elapsedMs, bool saturated, bool waiting)
        {
            if (elapsedMs >= 2.5)
            {
                Scale = Math.Max(1, Scale / 2);
                FrameGap = 2;
                m_StableBatches = 0;
                m_HealthyBatches = 0;
            }
            else if (!m_SlowFrame && !waiting && saturated && elapsedMs < 1.0)
            {
                if (++m_StableBatches >= 8)
                {
                    Scale = Math.Min(4, Scale + 1);
                    FrameGap = 1;
                    m_StableBatches = 0;
                }
            }
            else m_StableBatches = 0;
            if (!m_SlowFrame && !waiting && elapsedMs < 1.0)
            {
                if (++m_HealthyBatches >= 8) FrameGap = 1;
            }
            else m_HealthyBatches = 0;
            m_SlowFrame = false;
        }
    }
}
