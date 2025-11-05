// Src/Presentation/Views/Settings/ISettingsPage.cs
using System;

namespace NuvAI_FS.Src.Presentation.Views.Settings
{
    public interface ISettingsPage
    {
        bool IsDirty { get; }
        event EventHandler? DirtyChanged;
        /// <summary> Persiste los cambios (debe lanzar si hay error). </summary>
        void ApplyChanges();
        /// <summary> Limpia el estado sucio tras guardar. </summary>
        void ResetDirty();
    }
}
