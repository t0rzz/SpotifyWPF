using System.Collections.Generic;
using System.Linq;
using AutoMapper;
using AutoMapper.Configuration;
using SpotifyAPI.Web;
using SpotifyWPF.Model.Dto;

namespace SpotifyWPF.Model
{
    // ReSharper disable once ClassNeverInstantiated.Global
    public class AutoMapperConfiguration
    {
        public static MapperConfiguration Configure()
        {
            var config = new MapperConfiguration(cfg =>
            {
                cfg.CreateMap<PlaylistTrack<IPlayableItem>, TrackModel>()
                    .ForMember(dest => dest.Id,
                        act => act.MapFrom((src, dest) => (src.Track as FullTrack)?.Id ?? string.Empty))
                    .ForMember(dest => dest.Uri,
                        act => act.MapFrom((src, dest) => (src.Track as FullTrack)?.Uri ?? string.Empty))
                    .ForMember(dest => dest.Title,
                        act => act.MapFrom((src, dest) => (src.Track as FullTrack)?.Name ?? string.Empty))
                    .ForMember(dest => dest.Artist, act => act.MapFrom((src, dest) =>
                    {
                        var fullTrack = src.Track as FullTrack;
                        var artists = string.Join(", ",
                            (fullTrack?.Artists ?? new List<SimpleArtist>()).Select(sa => sa.Name));
                        return artists;
                    }))
                    .ForMember(dest => dest.DurationMs, act => act.MapFrom((src, dest) => 
                        (src.Track as FullTrack)?.DurationMs ?? 0))
                    .ForMember(dest => dest.AlbumArtUri, act => act.MapFrom((src, dest) => 
                    {
                        var fullTrack = src.Track as FullTrack;
                        var images = fullTrack?.Album?.Images;
                        if (images != null && images.Count > 0)
                        {
                            return new Uri(images[0].Url);
                        }
                        return null;
                    }))
                    .ForMember(dest => dest.Position, opt => opt.Ignore());

                cfg.CreateMap<AlbumDto, Album>()
                    .ForMember(dest => dest.Id, act => act.MapFrom(src => src.Id))
                    .ForMember(dest => dest.Name, act => act.MapFrom(src => src.Name ?? string.Empty))
                    .ForMember(dest => dest.Artist, act => act.MapFrom(src => src.Artists ?? string.Empty))
                    .ForMember(dest => dest.ImageUrl, act => act.MapFrom(src => src.ImageUrl ?? string.Empty))
                    .ForMember(dest => dest.TotalTracks, act => act.MapFrom(src => src.TotalTracks))
                    .ForMember(dest => dest.AddedAt, act => act.MapFrom(src => src.AddedAt ?? DateTime.Now));
            });

            config.AssertConfigurationIsValid();

            return config;
        }
    }
}