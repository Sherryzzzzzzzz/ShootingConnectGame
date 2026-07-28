using System.Collections.Generic;
using UnityEngine;

namespace CombatGirlsCharacterPack
{
    public class ObjectToggle : MonoBehaviour
    {
        [SerializeField] private List<GameObject> objects; // 활성화/비활성화할 게임 오브젝트 리스트
        private int currentIndex = 0; // 현재 활성화된 오브젝트의 인덱스

        public void ToggleObjects()
        {
            if (objects.Count == 0)
                return; // 리스트가 비어 있는 경우, 아무 작업도 하지 않음

            // 현재 활성화된 오브젝트를 비활성화
            objects[currentIndex].SetActive(false);

            // 다음 오브젝트로 인덱스를 이동, 리스트 끝에 도달하면 처음으로 돌아감
            currentIndex = (currentIndex + 1) % objects.Count;

            // 다음 오브젝트를 활성화
            objects[currentIndex].SetActive(true);
        }
    }
}