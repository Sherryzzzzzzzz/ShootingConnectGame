using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;

namespace CombatGirlsCharacterPack
{
    public class DoubleObjectController : MonoBehaviour
    {
        public List<GameObject> objectsToActivate;
        public List<GameObject> objectsToDeactivate;
        public Button button;

        private bool isActive = true;

        private void Start()
        {
            if (button != null)
            {
                button.onClick.AddListener(OnButtonClick);
            }
            else
            {
                Debug.LogError("Button reference not assigned in MultiObjectController!");
            }

            // if (objectsToActivate.Count == 0 || objectsToDeactivate.Count == 0)
            // {
            //     Debug.LogWarning("No objects assigned to activate/deactivate in MultiObjectController!");
            // }
        }

        private void OnButtonClick()
        {
            isActive = !isActive;

            if (isActive)
            {
                SetObjectsState(objectsToActivate, true);
                SetObjectsState(objectsToDeactivate, false);
            }
            else
            {
                SetObjectsState(objectsToActivate, false);
                SetObjectsState(objectsToDeactivate, true);
            }
        }

        private void SetObjectsState(List<GameObject> objects, bool state)
        {
            foreach (GameObject obj in objects)
            {
                if (obj != null)
                {
                    obj.SetActive(state);
                }
                else
                {
                    Debug.LogWarning("One of the objects in the list is null!");
                }
            }
        }
    }
}