using UnityEngine;
using UnityEngine.UI;

namespace CombatGirlsCharacterPack
{
    [System.Serializable]
    public class ObjectGroup
    {
        public GameObject[] objectsToToggle;
        public Button[] buttons;
    }

    public class ObjectController : MonoBehaviour
    {
        public ObjectGroup[] objectGroups;

        private void Start()
        {
            // 각 그룹에 대해 클릭 이벤트 핸들러를 연결합니다.
            for (int groupIndex = 0; groupIndex < objectGroups.Length; groupIndex++)
            {
                ObjectGroup group = objectGroups[groupIndex];

                for (int buttonIndex = 0; buttonIndex < group.buttons.Length; buttonIndex++)
                {
                    int buttonIdx = buttonIndex; // 클로저에서 올바른 버튼 인덱스를 사용하기 위해 변수를 만듭니다.
                    group.buttons[buttonIdx].onClick.AddListener(() => ToggleObject(group, buttonIdx));
                }
            }
        }

        private void ToggleObject(ObjectGroup group, int buttonIndex)
        {
            // 클릭된 버튼에 해당하는 오브젝트를 켜거나 끕니다.
            if (buttonIndex >= 0 && buttonIndex < group.objectsToToggle.Length)
            {
                GameObject obj = group.objectsToToggle[buttonIndex];
                obj.SetActive(!obj.activeSelf); // 현재 상태를 반대로 변경합니다.
            }
        }
    }
}