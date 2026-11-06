namespace HippoCrm.Core;

public static partial class CuiMemoryPolicy
{
    // Remove owned presentation references; immutable strings/framework copies
    // cannot be overwritten safely. This is not a claim of whole-process erasure.
    public static void Release(object? objValue)
    {
        if (objValue is Session objSession) objSession.Dispose();
        // Snapshot owns all detail lists, including v2 RecentContacts and ContactSearch.
        else if (objValue is AnalyticsSnapshot objAnalytics) objAnalytics.Release();
        else if (objValue is ProfileImage objImage) objImage.Dispose();
        else if (objValue is System.Runtime.CompilerServices.ITuple objTuple)
        { for (var iIndex = 0; iIndex < objTuple.Length; iIndex++) Release(objTuple[iIndex]); }
        else if (objValue is Contact or Organization)
        {
            foreach (var objProperty in objValue.GetType().GetProperties().Where(objProperty => objProperty.CanWrite && objProperty.PropertyType == typeof(string)))
                objProperty.SetValue(objValue, "");
            if (objValue is Contact objContact) objContact.lstOrganizationIds.Clear();
        }
        else if (objValue is CuiLookupPage objPage) { Release(objPage.lstContacts); Release(objPage.lstOrganizations); }
        else if (objValue is TokenChoiceCatalog objCatalog) { Release(objCatalog.Tags); Release(objCatalog.Skills); Release(objCatalog.Services); }
        else if (objValue is ContactEditorData objEditor) { Release(objEditor.Organizations); Release(objEditor.Interactions); Release(objEditor.Employment); Release(objEditor.Contracts); }
        else if (objValue is System.Collections.IEnumerable lstValues && objValue is not string)
        {
            foreach (var objItem in lstValues) Release(objItem);
            if (objValue is Array arrValues) Array.Clear(arrValues);
            else if (objValue is System.Collections.IList lstMutable && !lstMutable.IsReadOnly && !lstMutable.IsFixedSize) lstMutable.Clear();
        }
    }
}
