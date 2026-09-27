using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace Qmod
{
    // Capture / push / restore du DOF Unity (CameraEffects.m_dof).
    // highResolution une fois par capture ; nearBlur chaque push, restauré.
    internal sealed class CameraDof
    {
        private FieldInfo dofField;
        private readonly Dictionary<string, FieldInfo> dofPropFields = new Dictionary<string, FieldInfo>();
        private bool saved;
        private bool wasEnabled;
        private bool wasAutoFocus;
        private bool wasForced;
        private object dofComponent;
        private float savedBlur;
        private float savedFocalSize;
        private float savedAperture;
        private float savedFocalLength;
        private object savedFocalTransform;
        private bool savedNearBlur;
        private bool savedHighResolution;
        private bool qualitySet;

        internal bool Saved
        {
            get { return saved; }
        }

        internal float SavedBlur
        {
            get { return savedBlur; }
        }

        internal float SavedFocalSize
        {
            get { return savedFocalSize; }
        }

        internal float SavedAperture
        {
            get { return savedAperture; }
        }

        internal float SavedFocalLength
        {
            get { return savedFocalLength; }
        }

        internal bool Capture(CameraEffects effects)
        {
            if (!effects)
            {
                return false;
            }

            object dof = GetDof(effects);
            if (dof == null)
            {
                return false;
            }

            if (saved)
            {
                return true;
            }

            wasEnabled = dof is Behaviour dofBehaviour && dofBehaviour.enabled;
            wasAutoFocus = effects.m_dofAutoFocus;
            wasForced = effects.m_forceDof;
            savedBlur = GetFloat(dof, "maxBlurSize");
            savedFocalSize = GetFloat(dof, "focalSize");
            savedAperture = GetFloat(dof, "aperture");
            savedFocalLength = GetFloat(dof, "focalLength");
            savedFocalTransform = GetValue(dof, "focalTransform");
            savedNearBlur = GetBool(dof, "nearBlur");
            savedHighResolution = GetBool(dof, "highResolution");
            saved = true;
            qualitySet = false;
            return true;
        }

        internal void Push(CameraEffects effects, float blur, float focalSize, float aperture, Transform focalTransform, float focalLength, bool nearBlur = true)
        {
            if (!effects)
            {
                return;
            }

            object dof = GetDof(effects);
            if (dof == null)
            {
                return;
            }

            effects.SetDof(true);
            effects.m_forceDof = true;
            effects.m_dofAutoFocus = false;
            SetValue(dof, "maxBlurSize", blur);
            SetValue(dof, "focalSize", focalSize);
            SetValue(dof, "aperture", aperture);
            SetValue(dof, "focalTransform", focalTransform);
            SetValue(dof, "focalLength", focalLength);
            SetValue(dof, "nearBlur", nearBlur);
            if (!qualitySet)
            {
                SetValue(dof, "highResolution", true);
                qualitySet = true;
            }
        }

        internal void Restore(CameraEffects effects)
        {
            if (!saved)
            {
                return;
            }

            saved = false;
            qualitySet = false;
            if (!effects)
            {
                dofComponent = null;
                return;
            }

            object dof = GetDof(effects);
            effects.m_forceDof = wasForced;
            effects.m_dofAutoFocus = wasAutoFocus;
            effects.SetDof(wasEnabled);
            if (dof != null)
            {
                SetValue(dof, "maxBlurSize", savedBlur);
                SetValue(dof, "focalSize", savedFocalSize);
                SetValue(dof, "aperture", savedAperture);
                SetValue(dof, "focalLength", savedFocalLength);
                SetValue(dof, "focalTransform", savedFocalTransform);
                SetValue(dof, "nearBlur", savedNearBlur);
                SetValue(dof, "highResolution", savedHighResolution);
            }

            dofComponent = null;
        }

        private object GetDof(CameraEffects effects)
        {
            if (dofComponent is Behaviour alive && alive)
            {
                return dofComponent;
            }

            dofComponent = null;
            if (dofField == null)
            {
                dofField = typeof(CameraEffects).GetField("m_dof")
                    ?? typeof(CameraEffects).GetField("m_dof", BindingFlags.Instance | BindingFlags.NonPublic);
            }

            dofComponent = dofField != null ? dofField.GetValue(effects) : null;
            return dofComponent;
        }

        private float GetFloat(object dof, string name)
        {
            object value = GetValue(dof, name);
            return value is float f ? f : 0f;
        }

        private bool GetBool(object dof, string name)
        {
            object value = GetValue(dof, name);
            return value is bool b && b;
        }

        private object GetValue(object dof, string name)
        {
            FieldInfo field = PropField(dof, name);
            return field != null ? field.GetValue(dof) : null;
        }

        private void SetValue(object dof, string name, object value)
        {
            FieldInfo field = PropField(dof, name);
            if (field != null)
            {
                field.SetValue(dof, value);
            }
        }

        private FieldInfo PropField(object dof, string name)
        {
            string key = dof.GetType().FullName + "#" + name;
            FieldInfo field;
            if (!dofPropFields.TryGetValue(key, out field))
            {
                field = dof.GetType().GetField(name)
                    ?? dof.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
                dofPropFields[key] = field;
            }

            return field;
        }
    }
}
