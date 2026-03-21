using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.XR;

public class ShapeGrabber : MonoBehaviour
{
    [HideInInspector] public bool isGrabbed;

    // Grab state
    Vector3 grabOffsetPos;
    Quaternion grabOffsetRot;
    bool wasTriggerDown;

    // Editor drag state
    bool rightDragging;
    bool middleDragging;
    Vector3 lastMousePos;

    void Update()
    {
        bool triggerDown = false;

#if UNITY_EDITOR
        // Right mouse drag = rotate, Middle mouse drag = move
        var mouse = Mouse.current;
        if (mouse != null)
        {
            if (mouse.rightButton.wasPressedThisFrame)
            {
                rightDragging = true;
                lastMousePos = mouse.position.ReadValue();
                isGrabbed = true;
            }
            if (mouse.rightButton.wasReleasedThisFrame)
            {
                rightDragging = false;
                if (!middleDragging) isGrabbed = false;
            }
            if (mouse.middleButton.wasPressedThisFrame)
            {
                middleDragging = true;
                lastMousePos = mouse.position.ReadValue();
                isGrabbed = true;
            }
            if (mouse.middleButton.wasReleasedThisFrame)
            {
                middleDragging = false;
                if (!rightDragging) isGrabbed = false;
            }

            if (rightDragging)
            {
                Vector3 curPos = mouse.position.ReadValue();
                Vector3 delta = curPos - lastMousePos;
                transform.Rotate(Vector3.up, delta.x * 0.3f, Space.World);
                transform.Rotate(Vector3.right, -delta.y * 0.3f, Space.World);
                lastMousePos = curPos;
            }
            if (middleDragging)
            {
                Vector3 curPos = mouse.position.ReadValue();
                Vector3 delta = curPos - lastMousePos;
                var cam = Camera.main;
                if (cam != null)
                {
                    transform.position += cam.transform.right * delta.x * 0.001f
                                        + cam.transform.up * delta.y * 0.001f;
                }
                lastMousePos = curPos;
            }
        }
#else
        // Quest 3: right trigger to grab
        var rightCtrl = XRController.rightHand;
        if (rightCtrl == null) return;

        var trigger = rightCtrl.TryGetChildControl<AxisControl>("trigger");
        if (trigger == null) return;

        triggerDown = trigger.ReadValue() > 0.5f;

        if (triggerDown && !wasTriggerDown)
        {
            // Grab start — compute offset from controller to object
            GetControllerPose(rightCtrl, out Vector3 ctrlPos, out Quaternion ctrlRot);
            Quaternion invRot = Quaternion.Inverse(ctrlRot);
            grabOffsetPos = invRot * (transform.position - ctrlPos);
            grabOffsetRot = invRot * transform.rotation;
            isGrabbed = true;
        }
        else if (!triggerDown && wasTriggerDown)
        {
            // Release
            isGrabbed = false;
        }

        if (isGrabbed)
        {
            GetControllerPose(rightCtrl, out Vector3 ctrlPos, out Quaternion ctrlRot);
            transform.position = ctrlPos + ctrlRot * grabOffsetPos;
            transform.rotation = ctrlRot * grabOffsetRot;
        }

        wasTriggerDown = triggerDown;
#endif
    }

    void GetControllerPose(XRController ctrl, out Vector3 pos, out Quaternion rot)
    {
        pos = Vector3.zero;
        rot = Quaternion.identity;

        var posCtrl = ctrl.TryGetChildControl<Vector3Control>("devicePosition");
        if (posCtrl != null) pos = posCtrl.ReadValue();

        var rotCtrl = ctrl.TryGetChildControl<QuaternionControl>("deviceRotation");
        if (rotCtrl != null) rot = rotCtrl.ReadValue();
    }
}
