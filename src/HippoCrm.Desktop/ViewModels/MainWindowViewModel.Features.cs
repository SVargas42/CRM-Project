using System.Security.Cryptography;
using HippoCrm.Core;

namespace HippoCrm.Desktop.ViewModels;

public sealed partial class MainWindowViewModel
{
    private byte[]? arrCurrentContactImage;
    private byte[]? arrCurrentOrganizationImage;
    private CancellationTokenSource? objFeatureCancellation;
    public byte[]? arrContactImage=>arrCurrentContactImage;
    public byte[]? arrOrganizationImage=>arrCurrentOrganizationImage;
    public bool bHasContactImage=>arrContactImage is not null;
    public bool bHasOrganizationImage=>arrOrganizationImage is not null;
    public bool bCanUploadContactImage=>bCanEditContact && objCurrentContact is { Id:>0,Archived:false };
    public bool bCanUploadOrganizationImage=>bCanEditOrganization && objCurrentOrganization is { Id:>0,Archived:false };
    public string strContactLinks { get; private set; }="";
    public string strOrganizationLinks { get; private set; }="";
    public Func<IReadOnlyList<DuplicateCandidate>,bool,CancellationToken,Task<DuplicateDecision?>>? fnReviewDuplicates { get; set; }

    private void SetProfileImage(bool bContact,byte[]? arrImage)
    {
        var arrPrevious=bContact?arrCurrentContactImage:arrCurrentOrganizationImage;
        if(bContact) arrCurrentContactImage=arrImage; else arrCurrentOrganizationImage=arrImage;
        Notify(bContact?nameof(arrContactImage):nameof(arrOrganizationImage));
        Notify(bContact?nameof(bHasContactImage):nameof(bHasOrganizationImage));
        Notify(nameof(bCanUploadContactImage)); Notify(nameof(bCanUploadOrganizationImage));
        if(arrPrevious is not null && !ReferenceEquals(arrPrevious,arrImage)) CryptographicOperations.ZeroMemory(arrPrevious);
    }
    private void ClearFeatureState()
    {
        SetProfileImage(true,null); SetProfileImage(false,null);
        strContactLinks=""; strOrganizationLinks=""; Notify(nameof(strContactLinks)); Notify(nameof(strOrganizationLinks));
    }
    private async Task LoadRecordFeaturesAsync(bool bContact,long lngId)
    {
        SetProfileImage(bContact,null);
        var strLinks="";
        if(lngId>0)
        {
            var arrImage=await WorkAsync(()=>new ProfileImageService(objDatabase!,objSession!).Read(bContact?RecordKind.Contact:RecordKind.Organization,lngId));
            if(bLocking) { if(arrImage is not null) CryptographicOperations.ZeroMemory(arrImage); return; }
            SetProfileImage(bContact,arrImage);
            var lstLinks=await WorkAsync(()=>Crm.LinkedRecords(bContact?RecordKind.Contact:RecordKind.Organization,lngId));
            strLinks=lstLinks.Count==0?"":"Possible duplicate references: "+string.Join(", ",lstLinks.Select(objRow=>objRow.strName))+". These records have not been combined; their information remains separate.";
        }
        if(bContact) strContactLinks=strLinks; else strOrganizationLinks=strLinks;
        Notify(bContact?nameof(strContactLinks):nameof(strOrganizationLinks));
    }

    public Task ImportProfileImageAsync(bool bContact,Func<CancellationToken,Task<byte[]?>> fnSelect)=>ExecuteOperationAsync(async()=>
    {
        if(!(bContact?bCanUploadContactImage:bCanUploadOrganizationImage)) throw new ArgumentException("Save or restore an editable record before choosing a picture.");
        var lngId=bContact?objCurrentContact!.Id:objCurrentOrganization!.Id;
        using var objCancellation=new CancellationTokenSource(); objFeatureCancellation=objCancellation;
        byte[]? arrInput=null;
        try
        {
            var tskSelection=fnSelect(objCancellation.Token);
            try { arrInput=await tskSelection.WaitAsync(objCancellation.Token); }
            catch(OperationCanceledException) { _=ClearLateSelectionAsync(tskSelection); throw; }
            if(arrInput is null) return;
            await WorkAsync(()=>
            {
                var objImages=new ProfileImageService(objDatabase!,objSession!);
                return bContact?objImages.SaveContactPicture(lngId,arrInput,objCancellation.Token):objImages.SaveOrganizationLogo(lngId,arrInput,objCancellation.Token);
            });
            if(!bLocking)
            {
                await LoadRecordFeaturesAsync(bContact,lngId);
                strMessage="Picture saved. Previous versions are retained.";
            }
        }
        catch(OperationCanceledException) { }
        finally { objFeatureCancellation=null; if(arrInput is not null) CryptographicOperations.ZeroMemory(arrInput); }
    });

    private static async Task ClearLateSelectionAsync(Task<byte[]?> tskSelection)
    {
        try { var arrLate=await tskSelection; if(arrLate is not null) CryptographicOperations.ZeroMemory(arrLate); }
        catch(Exception) { /* Observe a canceled/failed picker; no file names or payloads enter diagnostics. */ }
    }

    private async Task<(bool bContinue,DuplicateDecision? objDecision)> ReviewDuplicatesAsync<T>(T objRecord,bool bContact)
    {
        var lstMatches=await WorkAsync(()=>bContact?Crm.PossibleDuplicates((Contact)(object)objRecord!):Crm.PossibleDuplicates((Organization)(object)objRecord!));
        if(lstMatches.Count==0) return (true,null);
        if(fnReviewDuplicates is null) throw new ArgumentException("Possible duplicates require review before saving.");
        using var objCancellation=new CancellationTokenSource(); objFeatureCancellation=objCancellation;
        try
        {
            var objDecision=await fnReviewDuplicates(lstMatches,bContact,objCancellation.Token).WaitAsync(objCancellation.Token);
            if(objDecision is null || bLocking || bClosed) return (false,null);
            return (true,objDecision);
        }
        catch(OperationCanceledException) { return (false,null); }
        finally { objFeatureCancellation=null; }
    }
}
