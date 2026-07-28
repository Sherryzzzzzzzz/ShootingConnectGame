using System.Collections.Generic;
using UnityEngine;

namespace CombatGirlsCharacterPack
{
    public class MaterialChanger : MonoBehaviour
    {
        [SerializeField] private List<SkinnedMeshRenderer> characterMeshRenderers; // 여러 SkinnedMeshRenderer들을 등록할 수 있는 리스트
        [SerializeField] private List<Material> materials; // 변경할 여러 머티리얼을 등록할 수 있는 리스트

        private int currentMaterialIndex = 0; // 현재 선택된 머티리얼의 인덱스

        public void ChangeMaterial()
        {
            if (materials.Count == 0 || characterMeshRenderers.Count == 0)
                return; // 리스트가 비어 있는 경우, 아무 작업도 하지 않음

            // 현재 인덱스에 해당하는 머티리얼을 모든 SkinnedMeshRenderer에 적용
            foreach (SkinnedMeshRenderer renderer in characterMeshRenderers)
            {
                renderer.material = materials[currentMaterialIndex];
            }

            // 다음 머티리얼로 인덱스를 이동, 리스트 끝에 도달하면 처음으로 돌아감
            currentMaterialIndex = (currentMaterialIndex + 1) % materials.Count;
        }
    }
}