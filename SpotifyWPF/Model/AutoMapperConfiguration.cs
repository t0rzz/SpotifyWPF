using System.Collections.Generic;
using System.Linq;
using AutoMapper;
using SpotifyAPI.Web;

namespace SpotifyWPF.Model
{
    // ReSharper disable once ClassNeverInstantiated.Global
    public class AutoMapperConfiguration
    {
        public static MapperConfiguration Configure()
        {
            var config = new MapperConfiguration(cfg =>
            {
                cfg.CreateMap<PlaylistTrack<IPlayableItem>, Track>()
                    .ForMember(dest => dest.Id,
                        act => act.MapFrom((src, dest) => (src.Track as FullTrack)?.Id ?? string.Empty))
                    .ForMember(dest => dest.Uri,
                        act => act.MapFrom((src, dest) => (src.Track as FullTrack)?.Uri ?? string.Empty))
                    .ForMember(dest => dest.TrackName,
                        act => act.MapFrom((src, dest) => (src.Track as FullTrack)?.Name))
                    .ForMember(dest => dest.Artists, act => act.MapFrom((src, dest) =>
                    {
                        var fullTrack = src.Track as FullTrack;

                        var artists = string.Join(", ",
                            (fullTrack?.Artists ?? new List<SimpleArtist>()).Select(sa => sa.Name));

                        return $"{artists}";
                    }))
                    .ForMember(dest => dest.AlbumName, act => act.MapFrom((src, dest) => 
                        (src.Track as FullTrack)?.Album?.Name ?? string.Empty))
                    .ForMember(dest => dest.DurationMs, act => act.MapFrom((src, dest) => 
                        (src.Track as FullTrack)?.DurationMs ?? 0))
                    .ForMember(dest => dest.ImageUrl, act => act.MapFrom((src, dest) => 
                    {
                        var fullTrack = src.Track as FullTrack;
                        var images = fullTrack?.Album?.Images;
                        return images != null && images.Count > 0 ? images[0].Url : string.Empty;
                    }));
            });

            config.AssertConfigurationIsValid();

            return config;
        }
    }
}