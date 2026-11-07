using HippoCrm.Core;
using HippoCrm.Desktop.Views;

namespace HippoCrm.Desktop;

public sealed partial class MainWindow
{
    private DuplicateReviewWindow? frmDuplicateReview;
    private void ConnectFeatureDialogs()
    {
        vmMain.fnReviewDuplicates??=async(lstMatches,bContact,ctCancellation)=>
        {
            if(!IsVisible || frmDuplicateReview is not null || vmMain.bLocking) return null;
            var frmReview=new DuplicateReviewWindow(lstMatches,bContact?vmMain.objContact?.Id==0:vmMain.objOrganization?.Id==0); frmDuplicateReview=frmReview;
            try { return await frmReview.ShowDialog<DuplicateDecision?>(this).WaitAsync(ctCancellation); }
            finally { frmDuplicateReview=null; if(frmReview.IsVisible) frmReview.Close(null); }
        };
        Closed+=(objSender,objArguments)=>{ frmDuplicateReview?.Close(null); vmMain.fnReviewDuplicates=null; };
    }
}
