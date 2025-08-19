namespace TheDiscDb.Data.Import
{
    using System;
    using TheDiscDb.InputModels;

    public class NameCollisionException : Exception
    {
        public NameCollisionException(string name, string imdbId, Group group)
        {
            Name = name;
            ImdbId = imdbId;
            Group = group;
        }

        public string Name { get; }
        public string ImdbId { get; }
        public Group Group { get; }
    }
}
