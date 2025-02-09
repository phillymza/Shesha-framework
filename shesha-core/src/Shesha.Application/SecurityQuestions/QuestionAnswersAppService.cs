﻿using Abp.Configuration;
using Abp.Domain.Repositories;
using Abp.UI;
using Shesha.Authorization.Users;
using Shesha.Configuration;
using Shesha.Domain;
using Shesha.SecurityQuestions.Dto;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Shesha.SecurityQuestions
{
    public class QuestionAnswersAppService : SheshaCrudServiceBase<QuestionAssignment, UserAnswerDto, Guid>
    {
        private readonly IRepository<User, long> _userRepository;
        private readonly ISettingManager _settingManager;

        public QuestionAnswersAppService(IRepository<QuestionAssignment, Guid> repository,
            IRepository<User, long> userRepository,
            ISettingManager settingManager) : base(repository)
        {
            _userRepository = userRepository;
            _settingManager = settingManager;
        }

        public override async Task<UserAnswerDto> CreateAsync(UserAnswerDto input)
        {
            if (input.User == null || input.User.Id == 0)
            {
                throw new UserFriendlyException("User is required");
            }

            var user = await _userRepository.GetAsync(input.User.Id);


            var numberOfQuestionsAllowed = _settingManager.GetSettingValue<int>(SheshaSettingNames.Security.ResetPasswordWithSecurityQuestionsNumQuestionsAllowed);

            var numberOfQuestionsSelected = await Repository.CountAsync(q => q.User == user);

            if (numberOfQuestionsSelected >= numberOfQuestionsAllowed)
            {
                throw new UserFriendlyException($"Maximum of {numberOfQuestionsAllowed} questions allowed");
            }

            var alreadyAnsweredQuestions = await Repository.CountAsync(q => q.User == user && q.SelectedQuestion.Id == input.SelectedQuestion.Id);
            if (alreadyAnsweredQuestions != 0)
            {
                throw new UserFriendlyException("You have already answered this question");
            }

            var entity = await SaveOrUpdateEntityAsync<QuestionAssignment>(null, async item =>
            {
                ObjectMapper.Map(input, item);
            });

            if (numberOfQuestionsSelected == numberOfQuestionsAllowed - 1)
            {
                user.SecurityQuestionStatus = Domain.Enums.RefListSecurityQuestionStatus.Set;

                await _userRepository.UpdateAsync(user);
            }


            return ObjectMapper.Map<UserAnswerDto>(entity);

        }
    }
}
