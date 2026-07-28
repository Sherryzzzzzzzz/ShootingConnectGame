using UnityEngine;

namespace CombatGirlsCharacterPack
{
    public class WeaponMaterialChanger : MonoBehaviour
    {
        // 변경할 머테리얼 배열
        public Material[] materials;
        // 변경할 3D 오브젝트 (예: 무기)
        public GameObject targetObject;

        // 현재 선택된 머테리얼 인덱스
        private int currentMaterialIndex = 0;

        // 버튼 클릭 시 호출할 함수
        public void ChangeMaterial()
        {
            if (materials.Length == 0 || targetObject == null) return;

            // MeshRenderer 컴포넌트 가져오기
            MeshRenderer renderer = targetObject.GetComponent<MeshRenderer>();

            if (renderer != null)
            {
                // 다음 머테리얼로 변경
                currentMaterialIndex = (currentMaterialIndex + 1) % materials.Length;
                renderer.material = materials[currentMaterialIndex];
            }
        }
    }
}