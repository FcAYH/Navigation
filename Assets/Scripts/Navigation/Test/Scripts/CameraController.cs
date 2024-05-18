using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Navigation.Test
{
    /// <summary>
    /// 利用 W A S D 和鼠标控制摄像机的移动和朝向
    /// 很简易的实现
    /// </summary>
    public class CameraController : MonoBehaviour
    {
        void Update()
        {
            MovePosition();
            MoveRotation();
        }

        private void MovePosition()
        {
            Vector3 move = new Vector3
            {
                x = Input.GetAxis("Horizontal"),
                z = Input.GetAxis("Vertical")
            };

            move = transform.TransformDirection(move);
            move *= Time.deltaTime * 10;

            transform.localPosition += move;
        }

        private void MoveRotation()
        {
            var euler = transform.localEulerAngles;
            euler.y += Input.GetAxis("Mouse X") * 2f;
            euler.x -= Input.GetAxis("Mouse Y") * 2f;

            Quaternion rotation = Quaternion.Euler(euler);

            //设置摄像机的位置与旋转
            transform.rotation = rotation;
        }
    }
}