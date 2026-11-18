using System;
using System.Collections.Generic;

namespace MarqueeDelete
{
    [Flags]
    public enum UndoAssetMask
    {
        None = 0,
        Buildings = 1 << 0,
        Networks = 1 << 1,
        Areas = 1 << 2,
        Trees = 1 << 3,
        Props = 1 << 4,
        Decals = 1 << 5,
        All = Buildings | Networks | Areas | Trees | Props | Decals,
    }

    public struct UndoCategoryCounts : IEquatable<UndoCategoryCounts>
    {
        public int Buildings;
        public int Networks;
        public int Areas;
        public int Trees;
        public int Props;
        public int Decals;

        public int Total => Buildings + Networks + Areas + Trees + Props + Decals;

        public void Add(SelectionCategory category, int delta)
        {
            if (delta <= 0)
            {
                return;
            }

            switch (category)
            {
                case SelectionCategory.Building: Buildings += delta; break;
                case SelectionCategory.Network: Networks += delta; break;
                case SelectionCategory.Area: Areas += delta; break;
                case SelectionCategory.Tree: Trees += delta; break;
                case SelectionCategory.Prop: Props += delta; break;
                case SelectionCategory.Decal: Decals += delta; break;
            }
        }

        public int GetFilteredSum(UndoAssetMask mask)
        {
            int sum = 0;
            if ((mask & UndoAssetMask.Buildings) != 0) sum += Buildings;
            if ((mask & UndoAssetMask.Networks) != 0) sum += Networks;
            if ((mask & UndoAssetMask.Areas) != 0) sum += Areas;
            if ((mask & UndoAssetMask.Trees) != 0) sum += Trees;
            if ((mask & UndoAssetMask.Props) != 0) sum += Props;
            if ((mask & UndoAssetMask.Decals) != 0) sum += Decals;
            return sum;
        }

        public bool Equals(UndoCategoryCounts other)
        {
            return Buildings == other.Buildings &&
                   Networks == other.Networks &&
                   Areas == other.Areas &&
                   Trees == other.Trees &&
                   Props == other.Props &&
                   Decals == other.Decals;
        }

        public override bool Equals(object obj) => obj is UndoCategoryCounts other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = Buildings;
                hash = (hash * 397) ^ Networks;
                hash = (hash * 397) ^ Areas;
                hash = (hash * 397) ^ Trees;
                hash = (hash * 397) ^ Props;
                hash = (hash * 397) ^ Decals;
                return hash;
            }
        }

        public static bool operator ==(UndoCategoryCounts left, UndoCategoryCounts right) => left.Equals(right);
        public static bool operator !=(UndoCategoryCounts left, UndoCategoryCounts right) => !left.Equals(right);
    }

    public static class SelectiveUndoClassifier
    {
        public static UndoAssetMask ToMaskBit(SelectionCategory category)
        {
            switch (category)
            {
                case SelectionCategory.Building: return UndoAssetMask.Buildings;
                case SelectionCategory.Network: return UndoAssetMask.Networks;
                case SelectionCategory.Area: return UndoAssetMask.Areas;
                case SelectionCategory.Tree: return UndoAssetMask.Trees;
                case SelectionCategory.Prop: return UndoAssetMask.Props;
                case SelectionCategory.Decal: return UndoAssetMask.Decals;
                default: return UndoAssetMask.None;
            }
        }

        public static bool IsIncluded(SelectionCategory category, UndoAssetMask mask)
        {
            UndoAssetMask bit = ToMaskBit(category);
            return bit != UndoAssetMask.None && (mask & bit) != 0;
        }

        public static bool ShouldCaptureEdgeAsStandalone(bool hasOwner, bool isOwnerInSubmitted)
        {
            // If an edge has an owner and that owner is also in the deletion plan (e.g. building),
            // it is captured and restored as a child via CaptureSubNets rather than as a standalone top-level net.
            if (hasOwner && isOwnerInSubmitted)
            {
                return false;
            }
            return true;
        }
    }

    public static class SelectiveUndoLogic
    {
        public static bool ShouldCaptureEdgeAsStandalone(bool hasOwner, bool isOwnerInSubmitted)
            => SelectiveUndoClassifier.ShouldCaptureEdgeAsStandalone(hasOwner, isOwnerInSubmitted);
    }

    public interface IUndoSnapshotMeta
    {
        UndoCategoryCounts Counts { get; }
        UndoCategoryCounts NotCaptured { get; }
        int TotalCount { get; }
        bool Truncated { get; }
        bool HasOwnerTimeout { get; }
        int RestoredCount { get; }
        int SkippedCount { get; }
    }

    public struct SelectiveUndoUiSnapshot : IEquatable<SelectiveUndoUiSnapshot>
    {
        public bool HasActiveUndo;
        public bool IsInProgress;
        public bool IsPaused;
        public bool CanUndo;
        public int HistoryDepth;
        public bool ZeroMatch;

        public UndoAssetMask EffectiveMask;
        public UndoCategoryCounts Counts;
        public int NextCount;
        public int NotCapturedCount;

        public int RestoredCount;
        public int SkippedCount;
        public int ActiveTotalCount;

        public string Notice;
        public int CaptureFailedCount;

        public string FinishedNotice;
        public int FinishedRestoredCount;
        public int FinishedSkippedCount;
        public int FinishedTotalCount;
        public int FinishedNotCapturedCount;

        public bool Equals(SelectiveUndoUiSnapshot other)
        {
            return HasActiveUndo == other.HasActiveUndo &&
                   IsInProgress == other.IsInProgress &&
                   IsPaused == other.IsPaused &&
                   CanUndo == other.CanUndo &&
                   HistoryDepth == other.HistoryDepth &&
                   ZeroMatch == other.ZeroMatch &&
                   EffectiveMask == other.EffectiveMask &&
                   Counts == other.Counts &&
                   NextCount == other.NextCount &&
                   NotCapturedCount == other.NotCapturedCount &&
                   RestoredCount == other.RestoredCount &&
                   SkippedCount == other.SkippedCount &&
                   ActiveTotalCount == other.ActiveTotalCount &&
                   string.Equals(Notice, other.Notice, StringComparison.Ordinal) &&
                   CaptureFailedCount == other.CaptureFailedCount &&
                   string.Equals(FinishedNotice, other.FinishedNotice, StringComparison.Ordinal) &&
                   FinishedRestoredCount == other.FinishedRestoredCount &&
                   FinishedSkippedCount == other.FinishedSkippedCount &&
                   FinishedTotalCount == other.FinishedTotalCount &&
                   FinishedNotCapturedCount == other.FinishedNotCapturedCount;
        }

        public override bool Equals(object obj) => obj is SelectiveUndoUiSnapshot other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = HistoryDepth;
                hash = (hash * 397) ^ Counts.GetHashCode();
                hash = (hash * 397) ^ NextCount;
                hash = (hash * 397) ^ RestoredCount;
                hash = (hash * 397) ^ SkippedCount;
                hash = (hash * 397) ^ FinishedTotalCount;
                return hash;
            }
        }

        public static bool operator ==(SelectiveUndoUiSnapshot left, SelectiveUndoUiSnapshot right) => left.Equals(right);
        public static bool operator !=(SelectiveUndoUiSnapshot left, SelectiveUndoUiSnapshot right) => !left.Equals(right);
    }

    public sealed class SelectiveUndoTracker<TSnapshot> where TSnapshot : class, IUndoSnapshotMeta
    {
        private readonly List<TSnapshot> m_History;
        private readonly int m_MaxHistory;
        private TSnapshot m_Active;
        private UndoAssetMask m_ActiveMask;
        private int m_ActiveTotalCount;
        private bool m_Paused;

        private string m_Notice = string.Empty;
        private int m_CaptureFailedCount;

        private string m_FinishedNotice = string.Empty;
        private int m_FinishedRestoredCount;
        private int m_FinishedSkippedCount;
        private int m_FinishedTotalCount;
        private int m_FinishedNotCapturedCount;

        public SelectiveUndoTracker(int maxHistory = 5)
        {
            m_MaxHistory = maxHistory > 0 ? maxHistory : 5;
            MaxHistory = m_MaxHistory;
            m_History = new List<TSnapshot>(m_MaxHistory);
        }

        public int MaxHistory { get; set; }

        public TSnapshot ActiveSnapshot => m_Active;
        public UndoAssetMask ActiveMask => m_ActiveMask;
        public int ActiveTotalCount => m_Active != null ? m_ActiveTotalCount : 0;
        public bool HasActiveUndo => m_Active != null;
        public bool IsPaused => m_Active != null && m_Paused;
        public bool IsInProgress => m_Active != null && !m_Paused;
        public int HistoryDepth => m_History.Count;
        public string Notice => m_Notice;
        public string FinishedNotice => m_FinishedNotice;
        public int CaptureFailedCount => m_CaptureFailedCount;

        public bool CanUndo(UndoAssetMask mask)
        {
            if (IsPaused)
            {
                return true;
            }

            if (m_Active != null || m_History.Count == 0)
            {
                return false;
            }

            TSnapshot top = m_History[m_History.Count - 1];
            return top.Counts.GetFilteredSum(mask) > 0;
        }

        public int GetNextUndoCount(UndoAssetMask mask)
        {
            if (m_Active != null)
            {
                int rem = m_ActiveTotalCount - m_Active.RestoredCount - m_Active.SkippedCount;
                return rem > 0 ? rem : 0;
            }

            if (m_History.Count == 0)
            {
                return 0;
            }

            return m_History[m_History.Count - 1].Counts.GetFilteredSum(mask);
        }

        public int GetPreviewNotCapturedCount(UndoAssetMask mask)
        {
            if (m_Active != null)
            {
                return m_Active.NotCaptured.GetFilteredSum(m_ActiveMask);
            }

            if (m_History.Count == 0)
            {
                return 0;
            }

            return m_History[m_History.Count - 1].NotCaptured.GetFilteredSum(mask);
        }

        public SelectiveUndoUiSnapshot GetUiSnapshot(UndoAssetMask currentMask)
        {
            TSnapshot source = m_Active ?? (m_History.Count > 0 ? m_History[m_History.Count - 1] : null);
            UndoAssetMask effectiveMask = m_Active != null ? m_ActiveMask : currentMask;
            int nextCount = GetNextUndoCount(currentMask);
            bool hasActive = m_Active != null;
            bool paused = hasActive && m_Paused;
            bool inProgress = hasActive && !m_Paused;
            bool canUndo = CanUndo(currentMask);
            bool zeroMatch = !hasActive && m_History.Count > 0 && nextCount == 0;

            return new SelectiveUndoUiSnapshot
            {
                HasActiveUndo = hasActive,
                IsInProgress = inProgress,
                IsPaused = paused,
                CanUndo = canUndo,
                HistoryDepth = m_History.Count,
                ZeroMatch = zeroMatch,
                EffectiveMask = effectiveMask,
                Counts = source != null ? source.Counts : default(UndoCategoryCounts),
                NextCount = nextCount,
                NotCapturedCount = GetPreviewNotCapturedCount(currentMask),
                RestoredCount = hasActive ? m_Active.RestoredCount : 0,
                SkippedCount = hasActive ? m_Active.SkippedCount : 0,
                ActiveTotalCount = hasActive ? m_ActiveTotalCount : 0,
                Notice = m_Notice ?? string.Empty,
                CaptureFailedCount = m_CaptureFailedCount,
                FinishedNotice = m_FinishedNotice ?? string.Empty,
                FinishedRestoredCount = m_FinishedRestoredCount,
                FinishedSkippedCount = m_FinishedSkippedCount,
                FinishedTotalCount = m_FinishedTotalCount,
                FinishedNotCapturedCount = m_FinishedNotCapturedCount,
            };
        }

        public bool OnCaptureCompleted(TSnapshot snapshot)
        {
            if (snapshot == null)
            {
                return false;
            }

            ClearFinishedNotice();
            if (snapshot.TotalCount <= 0)
            {
                if (snapshot.NotCaptured.Total > 0)
                {
                    m_CaptureFailedCount = snapshot.NotCaptured.Total;
                    m_Notice = "UndoNothingCaptured";
                }
                else
                {
                    m_CaptureFailedCount = 0;
                    m_Notice = m_History.Count > 0
                        ? (m_History[m_History.Count - 1].Truncated ? "UndoPartial" : "UndoGeometryOnly")
                        : string.Empty;
                }
                return false;
            }

            m_CaptureFailedCount = 0;
            m_Notice = snapshot.Truncated ? "UndoPartial" : "UndoGeometryOnly";
            int effectiveLimit = MaxHistory > 0 ? MaxHistory : m_MaxHistory;
            while (m_History.Count >= effectiveLimit)
            {
                m_History.RemoveAt(0);
            }
            m_History.Add(snapshot);
            return true;
        }

        public bool TryStartOrResume(UndoAssetMask mask, out TSnapshot activeSnapshot, out UndoAssetMask activeMask, out bool isResume)
        {
            if (m_Active != null)
            {
                if (!m_Paused)
                {
                    activeSnapshot = null;
                    activeMask = UndoAssetMask.None;
                    isResume = false;
                    return false;
                }

                m_Paused = false;
                ClearFinishedNotice();
                m_CaptureFailedCount = 0;
                RestoreActiveNotice();
                activeSnapshot = m_Active;
                activeMask = m_ActiveMask;
                isResume = true;
                return true;
            }

            if (m_History.Count == 0)
            {
                activeSnapshot = null;
                activeMask = UndoAssetMask.None;
                isResume = false;
                return false;
            }

            TSnapshot top = m_History[m_History.Count - 1];
            int filteredTotal = top.Counts.GetFilteredSum(mask);
            if (filteredTotal <= 0)
            {
                activeSnapshot = null;
                activeMask = UndoAssetMask.None;
                isResume = false;
                return false;
            }

            ClearFinishedNotice();
            m_CaptureFailedCount = 0;
            m_History.RemoveAt(m_History.Count - 1);
            m_Active = top;
            m_ActiveMask = mask;
            m_ActiveTotalCount = filteredTotal;
            m_Paused = false;
            RestoreActiveNotice();
            activeSnapshot = m_Active;
            activeMask = m_ActiveMask;
            isResume = false;
            return true;
        }

        public bool Pause()
        {
            if (!IsInProgress)
            {
                return false;
            }

            m_Paused = true;
            m_Notice = "UndoPaused";
            return true;
        }

        public void SetNotice(string notice)
        {
            m_Notice = notice ?? string.Empty;
        }

        public void RestoreActiveNotice()
        {
            if (m_Active == null)
            {
                m_Notice = string.Empty;
                return;
            }

            m_Notice = m_Active.Truncated ? "UndoPartial" : "UndoGeometryOnly";
        }

        public void CompleteActive()
        {
            if (m_Active == null)
            {
                return;
            }

            TSnapshot snap = m_Active;
            int filteredNotCaptured = snap.NotCaptured.GetFilteredSum(m_ActiveMask);

            m_FinishedRestoredCount = snap.RestoredCount;
            m_FinishedSkippedCount = snap.SkippedCount;
            m_FinishedTotalCount = m_ActiveTotalCount;
            m_FinishedNotCapturedCount = filteredNotCaptured;
            m_FinishedNotice = snap.HasOwnerTimeout
                ? "UndoStalled"
                : (snap.SkippedCount > 0 || snap.Truncated || filteredNotCaptured > 0)
                    ? "UndoSubmittedPartial"
                    : "UndoSubmitted";

            m_Notice = string.Empty;
            m_Active = null;
            m_ActiveMask = UndoAssetMask.None;
            m_ActiveTotalCount = 0;
            m_Paused = false;
        }

        public void FailActive(string reason = "UndoError")
        {
            if (m_Active != null)
            {
                m_FinishedRestoredCount = m_Active.RestoredCount;
                m_FinishedSkippedCount = m_Active.SkippedCount;
                m_FinishedTotalCount = m_ActiveTotalCount;
                m_FinishedNotCapturedCount = m_Active.NotCaptured.GetFilteredSum(m_ActiveMask);
            }
            else
            {
                m_FinishedRestoredCount = 0;
                m_FinishedSkippedCount = 0;
                m_FinishedTotalCount = 0;
                m_FinishedNotCapturedCount = 0;
            }

            m_Active = null;
            m_ActiveMask = UndoAssetMask.None;
            m_ActiveTotalCount = 0;
            m_Paused = false;
            m_Notice = string.Empty;
            m_FinishedNotice = string.IsNullOrEmpty(reason) ? "UndoError" : reason;
        }

        public void ClearFinishedNotice()
        {
            m_FinishedNotice = string.Empty;
            m_FinishedRestoredCount = 0;
            m_FinishedSkippedCount = 0;
            m_FinishedTotalCount = 0;
            m_FinishedNotCapturedCount = 0;
        }

        public bool DismissNotices()
        {
            bool changed = !string.IsNullOrEmpty(m_FinishedNotice) || m_CaptureFailedCount > 0;
            if (!changed)
            {
                return false;
            }

            ClearFinishedNotice();
            m_CaptureFailedCount = 0;
            if (string.Equals(m_Notice, "UndoNothingCaptured", StringComparison.Ordinal))
            {
                m_Notice = m_History.Count > 0
                    ? (m_History[m_History.Count - 1].Truncated ? "UndoPartial" : "UndoGeometryOnly")
                    : string.Empty;
            }
            return true;
        }

        public void ClearAll()
        {
            m_History.Clear();
            m_Active = null;
            m_ActiveMask = UndoAssetMask.None;
            m_ActiveTotalCount = 0;
            m_Paused = false;
            m_Notice = string.Empty;
            m_CaptureFailedCount = 0;
            ClearFinishedNotice();
        }
    }
}
