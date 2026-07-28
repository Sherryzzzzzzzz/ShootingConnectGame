using UnityEngine;
using UnityEngine.EventSystems;

namespace CombatGirlsCharacterPack
{
    public class CameraController_AnimationReset : MonoBehaviour
    {
        public Transform target; // 중심으로 할 객체
        public float distance = 10f; // 카메라와 객체 사이의 거리
        public float heightOffset = 2f; // 카메라와 객체 사이의 높이
        public float sensitivity = 5f; // 마우스 감도
        public float rotationSpeedMultiplier = 0.2f; // 회전 속도 조절을 위한 변수
        public float zoomSpeed = 5f; // 줌 속도
        public float yAdjustmentSpeed = 0.2f; // y 값 조절 속도

        private float initialDistance;
        private float initialHeightOffset;
        private Vector3 initialPosition;
        private Quaternion initialRotation;
        private Vector3 initialTargetPosition;
        private Animator targetAnimator; // 타겟의 Animator 컴포넌트

        private float currentX = 0f;
        private float currentY = 0f;
        private Vector3 dragOrigin;
        private bool isDragging = false;
        private bool isMiddleClickDragging = false;
        private float middleClickDragOriginY;

        private void Start()
        {
            // 초기 카메라 각도 및 위치 설정
            initialDistance = distance;
            initialHeightOffset = heightOffset;
            initialPosition = transform.position;
            initialRotation = transform.rotation;
            initialTargetPosition = target.position;

            // 초기 카메라 각도 설정
            Vector3 angles = transform.eulerAngles;
            currentX = angles.y;
            currentY = angles.x;

            // 타겟의 Animator 컴포넌트 가져오기
            if (target != null)
            {
                targetAnimator = target.GetComponent<Animator>();
            }
        }

        private void Update()
        {
            // UI 요소와의 충돌을 확인하고 무시하기
            if (EventSystem.current.IsPointerOverGameObject())
            {
                // 마우스가 UI 요소 위에 있으면 카메라 컨트롤 동작을 무시
                //isDragging = false;
                //isMiddleClickDragging = false;
                //return;
            }

            // 마우스 입력 받기
            if (Input.GetMouseButtonDown(0))
            {
                isDragging = true;
                dragOrigin = Input.mousePosition;
            }
            else if (Input.GetMouseButtonUp(0))
            {
                isDragging = false;
            }

            if (Input.GetMouseButtonDown(2))
            {
                isMiddleClickDragging = true;
                middleClickDragOriginY = Input.mousePosition.y;
            }
            else if (Input.GetMouseButtonUp(2))
            {
                isMiddleClickDragging = false;
            }

            // 마우스 드래그로 카메라 회전하기
            if (isDragging)
            {
                Vector3 difference = Input.mousePosition - dragOrigin;
                currentX += difference.x * sensitivity * rotationSpeedMultiplier * Time.deltaTime;
                currentY -= difference.y * sensitivity * rotationSpeedMultiplier * Time.deltaTime;

                currentY = Mathf.Clamp(currentY, -90f, 90f);
            }

            // 마우스 중간 버튼 드래그로 y 값 조절하기
            if (isMiddleClickDragging)
            {
                float yDifference = (Input.mousePosition.y - middleClickDragOriginY) * yAdjustmentSpeed * Time.deltaTime;
                heightOffset -= yDifference; // heightOffset을 조절하여 카메라의 높이만 변경
                middleClickDragOriginY = Input.mousePosition.y; // 원점을 업데이트
            }

            // 줌 인/아웃 처리
            float zoomInput = Input.GetAxis("Mouse ScrollWheel");
            if (zoomInput != 0f)
            {
                distance -= zoomInput * zoomSpeed;
                distance = Mathf.Clamp(distance, 1f, 100f);
            }

            // 우클릭으로 리셋하기
            if (Input.GetMouseButtonDown(1))
            {
                ResetCamera();
            }

            // 카메라 위치와 회전 업데이트
            Quaternion rotation = Quaternion.Euler(currentY, currentX, 0);
            Vector3 offset = new Vector3(0f, heightOffset, 0f);
            transform.position = target.position + offset - rotation * Vector3.forward * distance;
            transform.LookAt(target.position + offset);
        }

        private void ResetCamera()
        {
            distance = initialDistance;
            heightOffset = initialHeightOffset;
            transform.position = initialPosition;
            transform.rotation = initialRotation;
            currentX = initialRotation.eulerAngles.y;
            currentY = initialRotation.eulerAngles.x;
            target.position = initialTargetPosition;

            // 타겟의 애니메이션 리셋
            if (targetAnimator != null)
            {
                targetAnimator.Play(targetAnimator.GetCurrentAnimatorStateInfo(0).fullPathHash, -1, 0f);
            }
        }
    }
}