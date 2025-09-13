namespace SpotifyWPF.Model.Dto
{
    public class PagingDto<T>
    {
        public string? Href { get; set; }
        public int Limit { get; set; }
        public int Offset { get; set; }
        public int Total { get; set; }
        public string? Next { get; set; }
        public string? Previous { get; set; }
        public System.Collections.Generic.List<T> Items { get; set; } = new System.Collections.Generic.List<T>();
    }
}
