using System;
using System.Collections.Generic;

namespace ShootingGame.Shared.Prediction
{
    /// <summary>
    /// A single prediction node in the causal prediction tree.
    /// When confirmed: node is removed, children are re-parented to root.
    /// When rejected: subtree is rolled back (post-order undo — children first, then self).
    /// </summary>
    public class PredictionContext
    {
        public uint PredictionId { get; }
        public uint ParentId { get; internal set; }

        /// <summary>Action to undo this prediction if rejected.</summary>
        public Action UndoAction { get; }

        /// <summary>Arbitrary data attached to this prediction.</summary>
        public object UserData { get; set; }

        /// <summary>Child prediction nodes (depend on this prediction's state).</summary>
        public List<PredictionContext> Children { get; } = new List<PredictionContext>();

        /// <summary>Timestamp when this prediction was created.</summary>
        public float CreatedTime { get; }

        public PredictionContext(uint id, uint parentId, Action undoAction, object userData = null)
        {
            PredictionId = id;
            ParentId = parentId;
            UndoAction = undoAction;
            UserData = userData;
            CreatedTime = UnityEngine.Time.unscaledTime;
        }

        /// <summary>Execute undo and reject all child predictions (post-order).</summary>
        public void Rollback()
        {
            // Depth-first: children first
            for (int i = Children.Count - 1; i >= 0; i--)
            {
                Children[i].Rollback();
            }
            Children.Clear();

            // Then self
            UndoAction?.Invoke();
        }
    }
}
