using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Dota2SentinelDomain;

namespace Dota2SentinelHost.Migrations
{
    [DbContext(typeof(Repository))]
    [Migration("20170704143730_Initial")]
    partial class Initial
    {
        protected override void BuildTargetModel(ModelBuilder modelBuilder)
        {
            modelBuilder
                .HasAnnotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.SerialColumn)
                .HasAnnotation("ProductVersion", "1.1.2");

            modelBuilder.Entity("Dota2SentinelDomain.DataTypes.Account", b =>
                {
                    b.Property<int>("Id")
                        .ValueGeneratedOnAdd();

                    b.Property<string>("AccountId")
                        .HasMaxLength(20);

                    b.Property<bool>("NewUser");

                    b.HasKey("Id");

                    b.ToTable("Accounts");
                });

            modelBuilder.Entity("Dota2SentinelDomain.DataTypes.AccountMatch", b =>
                {
                    b.Property<int>("Id")
                        .ValueGeneratedOnAdd();

                    b.Property<int>("AccountId");

                    b.Property<int>("MatchId");

                    b.HasKey("Id");

                    b.HasIndex("AccountId");

                    b.HasIndex("MatchId");

                    b.ToTable("AccountMatches");
                });

            modelBuilder.Entity("Dota2SentinelDomain.DataTypes.AccountName", b =>
                {
                    b.Property<int>("Id")
                        .ValueGeneratedOnAdd();

                    b.Property<int?>("AccountId");

                    b.Property<string>("Name")
                        .HasMaxLength(200);

                    b.HasKey("Id");

                    b.HasIndex("AccountId");

                    b.ToTable("AccountNames");
                });

            modelBuilder.Entity("Dota2SentinelDomain.DataTypes.Ban", b =>
                {
                    b.Property<int>("Id")
                        .ValueGeneratedOnAdd();

                    b.Property<int?>("AccountId");

                    b.Property<int>("Duration");

                    b.Property<DateTime>("Expires");

                    b.Property<int?>("MatchId");

                    b.Property<string>("Reason");

                    b.Property<DateTime>("Set");

                    b.Property<float>("Severity");

                    b.Property<int>("Type");

                    b.HasKey("Id");

                    b.HasIndex("AccountId");

                    b.HasIndex("MatchId");

                    b.ToTable("Bans");
                });

            modelBuilder.Entity("Dota2SentinelDomain.DataTypes.Match", b =>
                {
                    b.Property<int>("Id")
                        .ValueGeneratedOnAdd();

                    b.Property<DateTime>("Closed");

                    b.Property<string>("CustomMapName")
                        .HasMaxLength(30);

                    b.Property<string>("MatchId")
                        .HasMaxLength(20);

                    b.Property<DateTime>("Registered");

                    b.Property<int?>("RequestedById");

                    b.HasKey("Id");

                    b.HasIndex("RequestedById");

                    b.ToTable("Matches");
                });

            modelBuilder.Entity("Dota2SentinelDomain.DataTypes.OngoingMatch", b =>
                {
                    b.Property<int>("Id")
                        .ValueGeneratedOnAdd();

                    b.Property<string>("CustomMapName")
                        .HasMaxLength(30);

                    b.Property<DateTime>("LastCheck");

                    b.Property<string>("LobbyId")
                        .HasMaxLength(20);

                    b.Property<string>("RequestedBy")
                        .HasMaxLength(20);

                    b.Property<DateTime>("Started");

                    b.HasKey("Id");

                    b.ToTable("OngoingMatches");
                });

            modelBuilder.Entity("Dota2SentinelDomain.DataTypes.Player", b =>
                {
                    b.Property<int>("Id")
                        .ValueGeneratedOnAdd();

                    b.Property<string>("AccountId")
                        .HasMaxLength(20);

                    b.Property<int?>("MatchId");

                    b.Property<string>("Name")
                        .HasMaxLength(200);

                    b.HasKey("Id");

                    b.HasIndex("MatchId");

                    b.ToTable("Players");
                });

            modelBuilder.Entity("Dota2SentinelDomain.DataTypes.PlayerMatch", b =>
                {
                    b.Property<int>("Id")
                        .ValueGeneratedOnAdd();

                    b.Property<string>("MatchId")
                        .HasMaxLength(20);

                    b.Property<int?>("PlayerId");

                    b.HasKey("Id");

                    b.HasIndex("PlayerId");

                    b.ToTable("PlayerMatches");
                });

            modelBuilder.Entity("Dota2SentinelDomain.DataTypes.AccountMatch", b =>
                {
                    b.HasOne("Dota2SentinelDomain.DataTypes.Account", "Account")
                        .WithMany("Matches")
                        .HasForeignKey("AccountId")
                        .OnDelete(DeleteBehavior.Cascade);

                    b.HasOne("Dota2SentinelDomain.DataTypes.Match", "Match")
                        .WithMany("Players")
                        .HasForeignKey("MatchId")
                        .OnDelete(DeleteBehavior.Cascade);
                });

            modelBuilder.Entity("Dota2SentinelDomain.DataTypes.AccountName", b =>
                {
                    b.HasOne("Dota2SentinelDomain.DataTypes.Account", "Account")
                        .WithMany("AccountNames")
                        .HasForeignKey("AccountId");
                });

            modelBuilder.Entity("Dota2SentinelDomain.DataTypes.Ban", b =>
                {
                    b.HasOne("Dota2SentinelDomain.DataTypes.Account", "Account")
                        .WithMany("Bans")
                        .HasForeignKey("AccountId");

                    b.HasOne("Dota2SentinelDomain.DataTypes.Match", "Match")
                        .WithMany("Bans")
                        .HasForeignKey("MatchId");
                });

            modelBuilder.Entity("Dota2SentinelDomain.DataTypes.Match", b =>
                {
                    b.HasOne("Dota2SentinelDomain.DataTypes.Account", "RequestedBy")
                        .WithMany("RequestedMatches")
                        .HasForeignKey("RequestedById");
                });

            modelBuilder.Entity("Dota2SentinelDomain.DataTypes.Player", b =>
                {
                    b.HasOne("Dota2SentinelDomain.DataTypes.OngoingMatch", "Match")
                        .WithMany("Players")
                        .HasForeignKey("MatchId");
                });

            modelBuilder.Entity("Dota2SentinelDomain.DataTypes.PlayerMatch", b =>
                {
                    b.HasOne("Dota2SentinelDomain.DataTypes.Player", "Player")
                        .WithMany("PlayerMatches")
                        .HasForeignKey("PlayerId");
                });
        }
    }
}
