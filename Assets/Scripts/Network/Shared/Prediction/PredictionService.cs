using System;
using System.Collections.Generic;
using UnityEngine;

namespace ShootingGame.Shared.Prediction
{
    /// <summary>
    /// Manages a causal tree of client-side predictions.
    /// Used for skills, ability chains, and other multi-step predicted operations
    /// where child operations depend on parent operation results.
    ///
    /// Coexists with the frame-level reconciliation in NetPlayerController —
    /// this handles chain predictions while reconciliation handles per-frame state.
    /// </summary>
    public class PredictionService
    {
        private readonly Dictionary<uint, PredictionContext> _pending = new Dictionary<uint, PredictionContext>();

        // Lazy-initialized: PredictionContext constructor calls Time.unscaledTime,
        // which is not allowed during MonoBehaviour construction.
        private PredictionContext _root;

        private uint _nextId = 1;

        /// <summary>Root node (id=0) for top-level predictions.</summary>
        public PredictionContext Root
        {
            get
            {
                if (_root == null)
                    _root = new PredictionContext(0, 0, null);
                return _root;
            }
        }

        /// <summary>Number of active predictions.</summary>
        public int PendingCount => _pending.Count;

        /// <summary>
        /// Create a new prediction node.
        /// </summary>
        /// <param name="parentId">Parent prediction ID (0 for root-level).</param>
        /// <param name="undoAction">Action to execute on rollback.</param>
        /// <param name="userData">Optional contextual data.</param>
        /// <returns>The created PredictionContext, or null if parent not found.</returns>
        public PredictionContext CreatePrediction(uint parentId, Action undoAction, object userData = null)
        {
            if (!_pending.ContainsKey(parentId) && parentId != 0)
            {
                Debug.LogWarning($"[PredictionService] Parent prediction {parentId} not found");
                parentId = 0; // Fall back to root
            }

            uint id = _nextId++;
            var context = new PredictionContext(id, parentId, undoAction, userData);

            PredictionContext parent = parentId == 0 ? Root : _pending[parentId];
            parent.Children.Add(context);

            _pending[id] = context;
            return context;
        }

        /// <summary>
        /// Confirm a prediction — it was accepted by the server.
        /// Removes the node and re-parents children to root.
        /// </summary>
        public void ConfirmPrediction(uint predictionId)
        {
            if (!_pending.TryGetValue(predictionId, out var context)) return;

            // Re-parent children to root (their base is now confirmed)
            foreach (var child in context.Children)
            {
                child.ParentId = 0;
                Root.Children.Add(child);
            }
            context.Children.Clear();

            _pending.Remove(predictionId);
        }

        /// <summary>
        /// Reject a prediction — the server denied it.
        /// Rolls back the entire subtree (post-order undo).
        /// </summary>
        public void RejectPrediction(uint predictionId)
        {
            if (!_pending.TryGetValue(predictionId, out var context)) return;

            // Detach from parent
            if (context.ParentId == 0)
                Root.Children.Remove(context);
            else if (_pending.TryGetValue(context.ParentId, out var parent))
                parent.Children.Remove(context);

            // Rollback subtree
            context.Rollback();

            // Remove subtree from pending
            RemoveSubtree(context);
        }

        /// <summary>
        /// Remove all predictions for cleanup.
        /// </summary>
        public void Clear()
        {
            foreach (var ctx in _pending.Values)
            {
                ctx.Children.Clear();
            }
            _pending.Clear();
            Root.Children.Clear();
        }

        private void RemoveSubtree(PredictionContext node)
        {
            _pending.Remove(node.PredictionId);
            foreach (var child in node.Children)
                RemoveSubtree(child);
            node.Children.Clear();
        }
    }
}
