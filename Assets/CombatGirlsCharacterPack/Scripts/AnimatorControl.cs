using UnityEngine;
using UnityEngine.UI;

namespace CombatGirlsCharacterPack
{
    public class AnimatorControl : MonoBehaviour
    {
        private Animator animator;
        public Toggle rootMotionToggle; // Toggle UI 컴포넌트를 연결합니다.

        private void Start()
        {
            // 캐릭터 프리팹의 Animator 컴포넌트에 접근합니다.
            animator = GetComponent<Animator>();

            // 토글 UI의 상태를 변경할 때마다 함수를 호출합니다.
            rootMotionToggle.onValueChanged.AddListener(ToggleRootMotion);
        }

        public void ToggleRootMotion(bool enableRootMotion)
        {
            // 어플라이 루트모션 옵션을 설정합니다.
            animator.applyRootMotion = enableRootMotion;
        }
    }
}