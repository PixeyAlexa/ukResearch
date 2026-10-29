using System.Globalization;
using System.Windows.Input;
using PrasaResarch.Commands;
using PrasaResarch.Models;
using PrasaResarch.Services;

namespace PrasaResarch.ViewModels;
public partial class MainViewModel
{
    public ICommand OpenReviewPdfCommand { get; private set; } = null!;
    public ICommand ConfirmRevenueCommand { get; private set; } = null!;
    public string[] ReviewEntities { get; } = ["Company", "Group"];
    private RevenueCandidate? selectedCandidate;
    public RevenueCandidate? SelectedCandidate
    {
        get => selectedCandidate;
        set
        {
            Set(ref selectedCandidate, value); Changed(nameof(CandidateEvidence));
            if (value == null) return;
            ReviewAmount = value.Exact && value.Year == Selected?.PeriodEnd?.Year ? value.Amount?.ToString(CultureInfo.InvariantCulture) ?? "" : ""; ReviewCurrency = value.Currency;
            ReviewEntity = value.Entity == "Unknown" ? "" : value.Entity; ReviewPage = value.Page?.ToString() ?? "";
            SourceInspected = false;
        }
    }
    public string CandidateEvidence => SelectedCandidate == null ? "Select a candidate to inspect its year headings, raw OCR/text, coordinates and validation issues." :
        $"{SelectedCandidate.Display}\nYear headings / statement context:\n{SelectedCandidate.Headings}\n\nRaw text:\n{SelectedCandidate.RawText}\n\nBounds: x={SelectedCandidate.Left:F1} to {SelectedCandidate.Right:F1}, y={SelectedCandidate.Bottom:F1}; scale={SelectedCandidate.Scale}; number/year/currency confidence={SelectedCandidate.Confidence:F1}; label confidence={SelectedCandidate.LabelConfidence:F1}; entity confidence={SelectedCandidate.EntityConfidence:F1}; aligned={SelectedCandidate.Aligned}; audited label={SelectedCandidate.Audited}";
    private string reviewAmount = "", reviewCurrency = "", reviewEntity = "", reviewPage = "", reviewPeriod = "", reviewReason = "", reviewHistory = "";
    private bool sourceInspected;
    public string ReviewAmount { get => reviewAmount; set => Set(ref reviewAmount, value); }
    public string ReviewCurrency { get => reviewCurrency; set => Set(ref reviewCurrency, value); }
    public string ReviewEntity { get => reviewEntity; set => Set(ref reviewEntity, value); }
    public string ReviewPage { get => reviewPage; set => Set(ref reviewPage, value); }
    public string ReviewPeriod { get => reviewPeriod; set => Set(ref reviewPeriod, value); }
    public string ReviewReason { get => reviewReason; set => Set(ref reviewReason, value); }
    public string ReviewHistory { get => reviewHistory; set => Set(ref reviewHistory, value); }
    public bool SourceInspected { get => sourceInspected; set => Set(ref sourceInspected, value); }
    private void ReviewSelectionChanged()
    {
        SelectedCandidate = null; SourceInspected = false; ReviewAmount = ""; ReviewCurrency = ""; ReviewEntity = ""; ReviewPage = "";
        ReviewPeriod = Selected?.PeriodEnd?.ToString("yyyy-MM-dd") ?? ""; ReviewReason = "";
        ReviewHistory = Selected == null ? "" : string.Join("\n", persistence.Reviews(Selected.CompanyNumber).Select(r => $"{r.At:O} | {r.Reviewer} | Page {r.Page} | {r.Entity} | {r.Currency} {r.Amount} | Period {r.PeriodEnd:yyyy-MM-dd}\n{r.Reason}\nSource: {r.SourceUrl}\nSHA256: {r.DocumentHash}"));
    }
    private void InitializeReview()
    {
        OpenReviewPdfCommand = new RelayCommand(_ =>
        {
            if (Selected == null) return;
            if (File.Exists(Selected.LocalDocumentPath)) OpenUrl(Selected.LocalDocumentPath);
            else if (Selected.SourceUrl.Length > 0) OpenUrl(Selected.SourceUrl);
            else Message = "Retry extraction to retrieve the original filing before reviewing.";
        }, _ => Selected != null);
        ConfirmRevenueCommand = new RelayCommand(_ =>
        {
            try
            {
                if (Selected == null) return;
                if (!System.Text.RegularExpressions.Regex.IsMatch(ReviewAmount, @"^[+-]?(?:\d+|\d{1,3}(?:,\d{3})+)(?:\.\d+)?$") || !decimal.TryParse(ReviewAmount, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount)) throw new ArgumentException("Enter the exact full monetary amount, with valid grouping and decimal point; do not enter rounded M/K figures.");
                if (!DateTime.TryParseExact(ReviewPeriod, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var period) || !int.TryParse(ReviewPage, out var page)) throw new ArgumentException("Enter a source page and reporting period in yyyy-MM-dd format.");
                var old = Selected;
                var result = new ManualRevenueReviewService(persistence).Confirm(old, amount, ReviewCurrency, period, ReviewEntity, page, ReviewReason, SourceInspected);
                Results[Results.IndexOf(old)] = result; Selected = result; NotifyCounts(); Message = "Manually verified revenue saved with source hash, reviewer, timestamp and audit history.";
            }
            catch (Exception ex) { ShowError(ex); }
        }, _ => !Busy && Selected != null);
    }
}

