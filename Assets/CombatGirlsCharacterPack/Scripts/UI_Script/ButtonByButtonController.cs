using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;

namespace CombatGirlsCharacterPack
{
    [System.Serializable]
    public struct GameObjectGroup
    {
        public Button button;
        public GameObject objectToActivate;
        public List<GameObject> objectsToDeactivate;
    }

    public class ButtonByButtonController : MonoBehaviour
    {
        public List<GameObjectGroup> groups = new List<GameObjectGroup>();

        private void Start()
        {
            foreach (var group in groups)
            {
                if (group.button != null)
                {
                    group.button.onClick.AddListener(() => OnButtonClick(group));
                }
                else
                {
                    Debug.LogError("Button reference is not assigned in the inspector for a group.");
                }
            }
        }

        private void OnButtonClick(GameObjectGroup group)
        {
            if (group.objectToActivate != null)
            {
                bool isActive = !group.objectToActivate.activeSelf;
                group.objectToActivate.SetActive(isActive);
            }

            foreach (var obj in group.objectsToDeactivate)
            {
                if (obj != null && obj.activeSelf)
                {
                    obj.SetActive(false);
                }
            }
        }
    }
}